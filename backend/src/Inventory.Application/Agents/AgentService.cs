using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Application.Abstractions;
using Inventory.Application.Assets;
using Inventory.Application.AssetTags;
using Inventory.Application.Common;
using Inventory.Domain.Agents;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Inventory.Domain.Common;

namespace Inventory.Application.Agents;

public sealed class AgentService
{
    private const string TokenPrefix = "inv_enroll";
    private const string KeyRecoveryTokenPrefix = "inv_recover";
    private static readonly JsonSerializerOptions SnapshotJsonOptions =
        CreateSnapshotJsonOptions();

    private readonly IAgentRepository _repository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly AssetTagPolicyService _assetTagPolicyService;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;
    private readonly AgentSecuritySettings _settings;

    public AgentService(
        IAgentRepository repository,
        IInventoryRepository inventoryRepository,
        AssetTagPolicyService assetTagPolicyService,
        IClock clock,
        ICurrentUserContext currentUser,
        AgentSecuritySettings settings)
    {
        _repository = repository;
        _inventoryRepository = inventoryRepository;
        _assetTagPolicyService = assetTagPolicyService;
        _clock = clock;
        _currentUser = currentUser;
        _settings = settings;
    }

    public async Task<CreatedAgentEnrollmentProfile> CreateEnrollmentProfileAsync(
        CreateAgentEnrollmentProfileCommand command,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        if (command.RequireTpm)
        {
            throw TpmAttestationUnavailable();
        }

        var now = _clock.UtcNow;
        EnsureUtc(command.ExpiresAtUtc, "expiresAtUtc");
        if (command.ExpiresAtUtc <= now ||
            command.ExpiresAtUtc > now.AddDays(_settings.MaximumEnrollmentProfileDays))
        {
            throw new RequestValidationException(
                "agent_enrollment_profile_expiry_invalid",
                $"Enrollment profiles must expire within {_settings.MaximumEnrollmentProfileDays} days.");
        }

        var profileId = Guid.NewGuid();
        var secret = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var token = $"{TokenPrefix}.{profileId:N}.{secret}";
        var profile = AgentEnrollmentProfile.Create(
            profileId,
            command.Name,
            HashEnrollmentToken(token),
            command.ExpiresAtUtc,
            command.MaxEnrollments,
            command.RequireTpm,
            command.AllowedHostnameSuffix,
            actor.UserId,
            now);
        _repository.Add(profile);
        _repository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "agent.enrollment_profile.created",
                "agent_enrollment_profile",
                profile.Id.ToString(),
                now,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        profile.Name,
                        profile.ExpiresAtUtc,
                        profile.MaxEnrollments,
                        profile.RequireTpm,
                        profile.AllowedHostnameSuffix,
                    }),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        return new CreatedAgentEnrollmentProfile(ToView(profile), token);
    }

    public async Task<AgentEnrollmentProfileView> RevokeEnrollmentProfileAsync(
        Guid profileId,
        Guid version,
        CancellationToken cancellationToken)
    {
        EnsureIdentifier(profileId, "profileId");
        EnsureVersion(version);
        var actor = _currentUser.RequireActor();
        var profile = await _repository.GetEnrollmentProfileForUpdateAsync(
                profileId,
                cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Agent enrollment profile",
                profileId);
        try
        {
            profile.Revoke(version, _clock.UtcNow);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainConflict(exception);
        }

        _repository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "agent.enrollment_profile.revoked",
                "agent_enrollment_profile",
                profile.Id.ToString(),
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        profile.Name,
                        profile.EnrolledCount,
                        profile.MaxEnrollments,
                    }),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        return ToView(profile);
    }

    public async Task<AgentEnrollmentResult> EnrollAsync(
        EnrollAgentCommand command,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        ValidateTimestamp(command.TimestampEpochSeconds, now);
        ValidateNonce(command.Nonce);
        EnsureSupportedAlgorithm(command.SignatureAlgorithm);
        var tokenReference = ParseEnrollmentToken(command.EnrollmentToken);
        var normalizedDeviceId = NormalizeKey(command.DeviceId);
        var normalizedThumbprint = NormalizeThumbprint(command.KeyThumbprintSha256);
        ValidatePublicKey(
            command.PublicKeyPem,
            normalizedThumbprint,
            command.Signature,
            BuildEnrollmentCanonical(command));

        await using var transaction =
            await _repository.BeginTransactionAsync(cancellationToken);
        try
        {
            if (tokenReference.Kind == AgentEnrollmentTokenKind.KeyRecovery)
            {
                return await RecoverAgentKeyAsync(
                    command,
                    tokenReference.Id,
                    normalizedDeviceId,
                    normalizedThumbprint,
                    now,
                    transaction,
                    cancellationToken);
            }

            var profile = await _repository.GetEnrollmentProfileForUpdateAsync(
                    tokenReference.Id,
                    cancellationToken)
                ?? throw InvalidEnrollment();
            if (!FixedTimeEquals(
                    profile.TokenHash,
                    HashEnrollmentToken(command.EnrollmentToken)))
            {
                throw InvalidEnrollment();
            }

            if (profile.RequireTpm)
            {
                throw TpmAttestationUnavailable();
            }

            await ReserveNonceAsync(
                AgentRequestNonceScope.Enrollment,
                profile.Id,
                command.Nonce,
                now,
                cancellationToken);
            var existing = await _repository.GetAgentByDeviceIdForUpdateAsync(
                normalizedDeviceId,
                cancellationToken);
            if (existing is not null)
            {
                if (!existing.IsRevoked &&
                    FixedTimeEquals(
                        existing.KeyThumbprintSha256,
                        normalizedThumbprint))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return new AgentEnrollmentResult(
                        existing.Id,
                        existing.DeviceId,
                        now,
                        existing.LastSequence + 1,
                        _settings.CheckInIntervalSeconds);
                }

                throw new ResourceConflictException(
                    "agent_device_already_enrolled",
                    "This device identifier is already enrolled with another key.");
            }

            try
            {
                profile.Consume(
                    command.Identity.Hostname,
                    command.KeyProtection,
                    now);
            }
            catch (DomainRuleException exception)
            {
                throw new RequestAuthenticationException(
                    exception.Code,
                    exception.Message);
            }

            var agent = ManagedAgent.Enroll(
                profile.Id,
                command.DeviceId,
                NormalizePem(command.PublicKeyPem),
                normalizedThumbprint,
                command.KeyProtection,
                command.Identity.Hostname,
                command.Identity.SerialNumber,
                command.Identity.BiosUuid,
                command.Identity.Manufacturer,
                command.Identity.Model,
                now);
            _repository.Add(agent);
            _repository.AddAuditLog(
                AuditLog.Create(
                    null,
                    "Endpoint agent",
                    "agent.enrolled",
                    "managed_agent",
                    agent.Id.ToString(),
                    now,
                    "succeeded",
                    JsonSerializer.Serialize(
                        new
                        {
                            agent.DeviceId,
                            agent.Hostname,
                            agent.SerialNumber,
                            agent.KeyProtection,
                            EnrollmentProfileId = profile.Id,
                        }),
                    command.IpAddress,
                    command.Client,
                    command.CorrelationId));
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AgentEnrollmentResult(
                agent.Id,
                agent.DeviceId,
                now,
                1,
                _settings.CheckInIntervalSeconds);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AgentCheckInResult> CheckInAsync(
        CheckInAgentCommand command,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        ValidateTimestamp(command.TimestampEpochSeconds, now);
        ValidateNonce(command.Nonce);
        ValidateSnapshot(command.Snapshot, now);
        var payloadBytes = SerializeSnapshot(command.Snapshot);
        if (payloadBytes.Length > AgentProtocolMaximumSnapshotBytes)
        {
            throw new RequestValidationException(
                "agent_snapshot_too_large",
                "The endpoint snapshot exceeds the one-megabyte limit.");
        }

        var computedBodyHash = Convert.ToBase64String(
            SHA256.HashData(payloadBytes));
        if (!FixedTimeEquals(computedBodyHash, command.BodySha256))
        {
            throw new RequestAuthenticationException(
                "agent_body_hash_invalid",
                "The endpoint snapshot hash was not accepted.");
        }

        await using var transaction =
            await _repository.BeginTransactionAsync(cancellationToken);
        try
        {
            var agent = await _repository.GetAgentForUpdateAsync(
                    command.AgentId,
                    cancellationToken)
                ?? throw InvalidAgentAuthentication();
            if (!string.Equals(
                    agent.DeviceIdNormalized,
                    NormalizeKey(command.DeviceId),
                    StringComparison.Ordinal) ||
                agent.IsRevoked)
            {
                throw InvalidAgentAuthentication();
            }

            VerifySignature(
                agent.PublicKeyPem,
                BuildCheckInCanonical(command),
                command.Signature);
            await ReserveNonceAsync(
                AgentRequestNonceScope.CheckIn,
                agent.Id,
                command.Nonce,
                now,
                cancellationToken);

            var existingSnapshot = await _repository.GetSnapshotAsync(
                agent.Id,
                command.Snapshot.SnapshotId,
                cancellationToken);
            if (existingSnapshot is not null)
            {
                if (existingSnapshot.Sequence != command.Sequence ||
                    !FixedTimeEquals(
                        existingSnapshot.BodySha256,
                        command.BodySha256))
                {
                    throw new ResourceConflictException(
                        "agent_snapshot_conflict",
                        "The snapshot identifier was already used with different content.");
                }

                await transaction.CommitAsync(cancellationToken);
                return new AgentCheckInResult(
                    Accepted: true,
                    Duplicate: true,
                    agent.Id,
                    existingSnapshot.SnapshotId,
                    now,
                    agent.LastSequence + 1,
                    agent.LinkedAssetId,
                    _settings.CheckInIntervalSeconds);
            }

            var latestDetailedSnapshot =
                await _repository.GetLatestDetailedSnapshotAsync(
                    agent.Id,
                    cancellationToken);
            var contentHash = HashSnapshotContent(command.Snapshot);
            try
            {
                agent.RecordCheckIn(
                    command.Sequence,
                    command.Snapshot.SnapshotId,
                    command.Snapshot.CollectedAtUtc,
                    now,
                    command.Snapshot.AgentVersion,
                    command.Snapshot.Hostname,
                    command.Snapshot.Hardware.SerialNumber,
                    command.Snapshot.Hardware.BiosUuid,
                    command.Snapshot.Hardware.Manufacturer,
                    command.Snapshot.Hardware.Model,
                    command.IpAddress,
                    TimeSpan.FromSeconds(
                        _settings.CheckInBurstWindowSeconds),
                    _settings.MaximumCheckInsPerAgentBurst,
                    TimeSpan.FromHours(
                        _settings.CheckInQuotaWindowHours),
                    _settings.MaximumCheckInsPerAgentQuotaWindow);
            }
            catch (DomainRuleException exception)
            {
                if (exception.Code == "agent_revoked")
                {
                    throw InvalidAgentAuthentication();
                }

                if (exception.Code is
                    "agent_check_in_burst_exceeded" or
                    "agent_check_in_quota_exceeded")
                {
                    throw new RequestRateLimitException(
                        exception.Code,
                        exception.Message,
                        GetCheckInRetryAfterSeconds(
                            agent,
                            exception.Code,
                            now));
                }

                throw new ResourceConflictException(
                    exception.Code,
                    exception.Message,
                    exception);
            }

            if (!agent.LinkedAssetId.HasValue &&
                agent.MatchState == ManagedAgentMatchState.Unmatched)
            {
                var biosGuid = Guid.TryParse(
                    command.Snapshot.Hardware.BiosUuid,
                    out var parsedBiosGuid)
                    ? parsedBiosGuid
                    : (Guid?)null;
                var normalizedSerial = string.IsNullOrWhiteSpace(
                    command.Snapshot.Hardware.SerialNumber)
                    ? null
                    : NormalizeKey(
                        command.Snapshot.Hardware.SerialNumber);
                var match = await _repository.FindUnlinkedAssetMatchAsync(
                    normalizedSerial,
                    biosGuid,
                    cancellationToken);
                if (match.AssetId.HasValue)
                {
                    agent.AutoLinkAsset(
                        match.AssetId.Value,
                        biosGuid.HasValue
                            ? "Automatically matched by BIOS UUID or serial number"
                            : "Automatically matched by serial number");
                    _repository.AddAuditLog(
                        AuditLog.Create(
                            null,
                            "Endpoint agent",
                            "agent.asset.auto_linked",
                            "managed_agent",
                            agent.Id.ToString(),
                            now,
                            "succeeded",
                            JsonSerializer.Serialize(
                                new
                                {
                                    AgentId = agent.Id,
                                    AssetId = match.AssetId.Value,
                                    agent.DeviceId,
                                }),
                            command.IpAddress,
                            command.Client,
                            command.CorrelationId));
                }
                else if (match.HasConflict)
                {
                    var wasAlreadyInConflict =
                        agent.MatchState ==
                        ManagedAgentMatchState.Conflict;
                    agent.MarkMatchConflict(
                        match.HasTerminalMatch
                            ? "The reported identifiers match a retired or disposed asset; manual review is required."
                            : "Multiple assets matched the reported BIOS UUID or serial number.");
                    if (!wasAlreadyInConflict)
                    {
                        _repository.AddAuditLog(
                            AuditLog.Create(
                                null,
                                "Endpoint agent",
                                "agent.asset.match_conflict",
                                "managed_agent",
                                agent.Id.ToString(),
                                now,
                                "review_required",
                                JsonSerializer.Serialize(
                                    new
                                    {
                                        AgentId = agent.Id,
                                        agent.DeviceId,
                                        SerialNumber = normalizedSerial,
                                        BiosGuid = biosGuid,
                                    }),
                                command.IpAddress,
                                command.Client,
                                command.CorrelationId));
                    }
                }
            }

            _repository.Add(
                ManagedAgentSnapshot.Create(
                    agent.Id,
                    command.Snapshot.SnapshotId,
                    command.Sequence,
                    command.Snapshot.CollectedAtUtc,
                    now,
                    command.BodySha256,
                    contentHash,
                    latestDetailedSnapshot is not null &&
                    FixedTimeEquals(
                        latestDetailedSnapshot.ContentSha256,
                        contentHash)
                        ? null
                        : Encoding.UTF8.GetString(payloadBytes),
                    command.Snapshot.Hostname,
                    command.Snapshot.Hardware.SerialNumber,
                    command.Snapshot.Hardware.BiosUuid,
                    command.Snapshot.AgentVersion));
            await SaveChangesAsync(cancellationToken);
            await _repository.ApplySnapshotRetentionAsync(
                agent.Id,
                _settings.MaximumSnapshotRecordsPerAgent,
                _settings.MaximumSnapshotPayloadBytesPerAgent,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AgentCheckInResult(
                Accepted: true,
                Duplicate: false,
                agent.Id,
                command.Snapshot.SnapshotId,
                now,
                agent.LastSequence + 1,
                agent.LinkedAssetId,
                _settings.CheckInIntervalSeconds);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PageResult<ManagedAgentListItem>> SearchAsync(
        string? query,
        bool includeRevoked,
        bool? isRevoked,
        ManagedAgentMatchState? matchState,
        ManagedAgentConnectivityStatus? connectivity,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ValidatePaging(page, pageSize);
        var normalizedQuery = string.IsNullOrWhiteSpace(query)
            ? null
            : query.Trim().ToUpperInvariant();
        if (normalizedQuery?.Length > 200)
        {
            throw new RequestValidationException(
                "agent_search_too_long",
                "Agent search text must be 200 characters or fewer.");
        }

        var (now, onlineCutoff, staleCutoff) =
            GetConnectivityBoundaries();
        var result = await _repository.SearchAsync(
            new ManagedAgentSearch(
                normalizedQuery,
                includeRevoked,
                isRevoked,
                matchState,
                connectivity,
                onlineCutoff,
                staleCutoff,
                page,
                pageSize),
            cancellationToken);
        return new PageResult<ManagedAgentListItem>(
            result.Items
                .Select(item => WithConnectivity(item, now))
                .ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public Task<ManagedAgentFleetSummary> GetFleetSummaryAsync(
        bool includeRevoked,
        CancellationToken cancellationToken)
    {
        var (_, onlineCutoff, staleCutoff) =
            GetConnectivityBoundaries();
        return _repository.GetFleetSummaryAsync(
            includeRevoked,
            onlineCutoff,
            staleCutoff,
            cancellationToken);
    }

    public Task<PageResult<AgentEnrollmentProfileView>>
        SearchEnrollmentProfilesAsync(
            bool includeRevoked,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
    {
        ValidatePaging(page, pageSize);
        return _repository.SearchEnrollmentProfilesAsync(
            includeRevoked,
            page,
            pageSize,
            cancellationToken);
    }

    public async Task<ManagedAgentDetail> GetAsync(
        Guid agentId,
        CancellationToken cancellationToken)
    {
        EnsureIdentifier(agentId, "agentId");
        return WithConnectivity(
            await _repository.GetDetailAsync(agentId, cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Managed agent",
                agentId));
    }

    public async Task<ManagedAgentDetail> GetByAssetIdAsync(
        Guid assetId,
        CancellationToken cancellationToken)
    {
        EnsureIdentifier(assetId, "assetId");
        return WithConnectivity(
            await _repository.GetByAssetIdAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Managed agent inventory for asset",
                assetId));
    }

    public async Task<ManagedAgentDetail> LinkAsync(
        LinkManagedAgentCommand command,
        CancellationToken cancellationToken)
    {
        EnsureIdentifier(command.AgentId, "agentId");
        EnsureIdentifier(command.AssetId, "assetId");
        EnsureVersion(command.Version);
        var actor = _currentUser.RequireActor();
        var agent = await _repository.GetAgentForUpdateAsync(
                command.AgentId,
                cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Managed agent",
                command.AgentId);
        var assetTarget = await _repository.GetAssetLinkTargetAsync(
            command.AssetId,
            cancellationToken);
        if (assetTarget is null)
        {
            throw new ResourceNotFoundException("Asset", command.AssetId);
        }

        if (assetTarget.IsTerminal)
        {
            throw new ResourceConflictException(
                "agent_asset_terminal",
                "Retired or disposed assets cannot be linked to endpoint agents.");
        }

        if (await _repository.AssetLinkedToOtherAgentAsync(
                command.AssetId,
                command.AgentId,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "asset_already_linked_to_agent",
                "The asset is already linked to another endpoint agent.");
        }

        try
        {
            agent.LinkAsset(command.AssetId, command.Reason, command.Version);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainConflict(exception);
        }

        _repository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "agent.asset.linked",
                "managed_agent",
                agent.Id.ToString(),
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        AgentId = agent.Id,
                        AssetId = command.AssetId,
                        Reason = command.Reason,
                    }),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        return WithConnectivity(
            await _repository.GetDetailAsync(
                    agent.Id,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "The linked agent could not be reloaded."));
    }

    public async Task<ManagedAgentDetail> UnlinkAsync(
        UnlinkManagedAgentCommand command,
        CancellationToken cancellationToken)
    {
        EnsureIdentifier(command.AgentId, "agentId");
        EnsureVersion(command.Version);
        var actor = _currentUser.RequireActor();
        var agent = await _repository.GetAgentForUpdateAsync(
                command.AgentId,
                cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Managed agent",
                command.AgentId);
        var previousAssetId = agent.LinkedAssetId;
        try
        {
            agent.UnlinkAsset(command.Reason, command.Version);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainConflict(exception);
        }

        _repository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "agent.asset.unlinked",
                "managed_agent",
                agent.Id.ToString(),
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        AgentId = agent.Id,
                        PreviousAssetId = previousAssetId,
                        command.Reason,
                    }),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        return WithConnectivity(
            await _repository.GetDetailAsync(
                    agent.Id,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "The unlinked agent could not be reloaded."));
    }

    public async Task<ManagedAgentDetail> DismissAsync(
        DismissManagedAgentCommand command,
        CancellationToken cancellationToken)
    {
        EnsureIdentifier(command.AgentId, "agentId");
        EnsureVersion(command.Version);
        var actor = _currentUser.RequireActor();
        var agent = await _repository.GetAgentForUpdateAsync(
                command.AgentId,
                cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Managed agent",
                command.AgentId);
        try
        {
            agent.DismissMatch(command.Reason, command.Version);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainConflict(exception);
        }

        _repository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "agent.match.dismissed",
                "managed_agent",
                agent.Id.ToString(),
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        AgentId = agent.Id,
                        command.Reason,
                    }),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        return WithConnectivity(
            await _repository.GetDetailAsync(
                    agent.Id,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "The dismissed agent could not be reloaded."));
    }

    public async Task<ManagedAgentDetail> RestoreReviewAsync(
        RestoreManagedAgentReviewCommand command,
        CancellationToken cancellationToken)
    {
        EnsureIdentifier(command.AgentId, "agentId");
        EnsureVersion(command.Version);
        var actor = _currentUser.RequireActor();
        var agent = await _repository.GetAgentForUpdateAsync(
                command.AgentId,
                cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Managed agent",
                command.AgentId);
        try
        {
            agent.RestoreMatchReview(command.Reason, command.Version);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainConflict(exception);
        }

        _repository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "agent.match.review_restored",
                "managed_agent",
                agent.Id.ToString(),
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        AgentId = agent.Id,
                        command.Reason,
                    }),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        return WithConnectivity(
            await _repository.GetDetailAsync(
                    agent.Id,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "The restored agent could not be reloaded."));
    }

    public async Task<CreateAndLinkManagedAgentAssetResult>
        CreateAndLinkAssetAsync(
            CreateAndLinkManagedAgentAssetCommand command,
            CancellationToken cancellationToken)
    {
        EnsureIdentifier(command.AgentId, "agentId");
        EnsureVersion(command.Version);
        await ValidateAssetReferencesAsync(
            command.Asset.CategoryId,
            command.Asset.LocationId,
            cancellationToken);

        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        await using var transaction =
            await _repository.BeginTransactionAsync(cancellationToken);
        try
        {
            var resolvedAssetTag =
                await _assetTagPolicyService.ResolveForCreateAsync(
                    command.Asset.AssetTag,
                    command.Asset.GenerateAssetTag,
                    cancellationToken);
            command = command with
            {
                Asset = command.Asset with { AssetTag = resolvedAssetTag },
            };
            await EnsureAssetIdentifiersAvailableAsync(
                resolvedAssetTag,
                command.Asset.SerialNumber,
                cancellationToken);

            var agent = await _repository.GetAgentForUpdateAsync(
                    command.AgentId,
                    cancellationToken)
                ?? throw new ResourceNotFoundException(
                    "Managed agent",
                    command.AgentId);
            if (agent.LinkedAssetId.HasValue)
            {
                throw new ResourceConflictException(
                    "agent_already_linked",
                    "The endpoint agent is already linked to an asset.");
            }

            Asset asset;
            try
            {
                asset = Asset.Create(
                    ToAssetDetails(command.Asset),
                    now);
                agent.LinkAsset(
                    asset.Id,
                    command.Reason,
                    command.Version);
            }
            catch (DomainRuleException exception)
            {
                throw exception.Code == "concurrency_conflict"
                    ? MapDomainConflict(exception)
                    : new RequestValidationException(
                        exception.Code,
                        exception.Message);
            }

            _inventoryRepository.AddAsset(asset);
            _inventoryRepository.AddAssetEvent(
                AssetEvent.Create(
                    asset.Id,
                    AssetEventType.AssetCreated,
                    now,
                    actor.UserId,
                    actor.DisplayName,
                    AssetEventSource.Api,
                    $"Asset {asset.AssetTag} was created from endpoint agent {agent.Hostname}.",
                    JsonSerializer.Serialize(
                        new
                        {
                            asset.AssetTag,
                            asset.CategoryId,
                            asset.Status,
                            asset.Condition,
                            asset.LocationId,
                            ManagedAgentId = agent.Id,
                            command.Reason,
                        }),
                    actor.CorrelationId));
            _repository.AddAuditLog(
                AuditLog.Create(
                    actor.UserId,
                    actor.DisplayName,
                    "agent.asset.created_and_linked",
                    "managed_agent",
                    agent.Id.ToString(),
                    now,
                    "succeeded",
                    JsonSerializer.Serialize(
                        new
                        {
                            AgentId = agent.Id,
                            AssetId = asset.Id,
                            asset.AssetTag,
                            command.Reason,
                        }),
                    actor.IpAddress,
                    actor.Client,
                    actor.CorrelationId));

            await SaveChangesAsync(cancellationToken);
            var assetView = await _inventoryRepository.GetAssetViewAsync(
                    asset.Id,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "The created asset could not be reloaded.");
            var agentDetail = await _repository.GetDetailAsync(
                    agent.Id,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "The linked agent could not be reloaded.");
            await transaction.CommitAsync(cancellationToken);
            return new CreateAndLinkManagedAgentAssetResult(
                assetView,
                WithConnectivity(agentDetail));
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PageResult<AgentKeyRecoveryAuthorizationView>>
        SearchKeyRecoveryAuthorizationsAsync(
            Guid agentId,
            bool includeClosed,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
    {
        EnsureIdentifier(agentId, "agentId");
        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        if (!await _repository.AgentExistsAsync(agentId, cancellationToken))
        {
            throw new ResourceNotFoundException("Managed agent", agentId);
        }

        var now = _clock.UtcNow;
        var result =
            await _repository.SearchKeyRecoveryAuthorizationsAsync(
                agentId,
                includeClosed,
                now,
                normalizedPage.Page,
                normalizedPage.PageSize,
                cancellationToken);
        return new PageResult<AgentKeyRecoveryAuthorizationView>(
            result.Items.Select(item => ToView(item, now)).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<CreatedAgentKeyRecovery> CreateKeyRecoveryAsync(
        CreateAgentKeyRecoveryCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequireTpm)
        {
            throw TpmAttestationUnavailable();
        }

        EnsureIdentifier(command.AgentId, "agentId");
        EnsureVersion(command.Version);
        EnsureUtc(command.ExpiresAtUtc, "expiresAtUtc");
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        if (command.ExpiresAtUtc <= now ||
            command.ExpiresAtUtc >
            now.AddMinutes(_settings.MaximumKeyRecoveryMinutes))
        {
            throw new RequestValidationException(
                "agent_key_recovery_expiry_invalid",
                "Key-recovery authorization must expire in the future and " +
                $"within {_settings.MaximumKeyRecoveryMinutes} minutes.");
        }

        await using var transaction =
            await _repository.BeginTransactionAsync(cancellationToken);
        try
        {
            var agent = await _repository.GetAgentForUpdateAsync(
                    command.AgentId,
                    cancellationToken)
                ?? throw new ResourceNotFoundException(
                    "Managed agent",
                    command.AgentId);
            var pending =
                await _repository.GetPendingKeyRecoveryAuthorizationForUpdateAsync(
                    command.AgentId,
                    cancellationToken);
            if (pending is not null)
            {
                if (pending.ExpiresAtUtc > now)
                {
                    throw new ResourceConflictException(
                        "agent_key_recovery_already_pending",
                        "This endpoint agent already has a pending key-recovery authorization.");
                }

                try
                {
                    pending.Revoke(
                        pending.Version,
                        actor.UserId,
                        "Expired before a replacement authorization was issued.",
                        now);
                }
                catch (DomainRuleException exception)
                {
                    throw MapDomainConflict(exception);
                }

                await SaveChangesAsync(cancellationToken);
            }

            var previousAssetId = agent.LinkedAssetId;
            var previousThumbprint = agent.KeyThumbprintSha256;
            try
            {
                agent.ApproveKeyRecovery(
                    command.Reason,
                    command.Version,
                    now);
            }
            catch (DomainRuleException exception)
            {
                throw MapDomainConflict(exception);
            }

            var authorizationId = Guid.NewGuid();
            var secret = Base64UrlEncode(
                RandomNumberGenerator.GetBytes(32));
            var token =
                $"{KeyRecoveryTokenPrefix}.{authorizationId:N}.{secret}";
            var authorization = AgentKeyRecoveryAuthorization.Create(
                authorizationId,
                agent.Id,
                HashEnrollmentToken(token),
                command.Reason,
                command.RequireTpm,
                agent.Version,
                actor.UserId,
                actor.DisplayName,
                now,
                command.ExpiresAtUtc);
            _repository.Add(authorization);
            _repository.AddAuditLog(
                AuditLog.Create(
                    actor.UserId,
                    actor.DisplayName,
                    "agent.key_recovery.approved",
                    "managed_agent",
                    agent.Id.ToString(),
                    now,
                    "succeeded",
                    JsonSerializer.Serialize(
                        new
                        {
                            AgentId = agent.Id,
                            AuthorizationId = authorization.Id,
                            command.Reason,
                            command.RequireTpm,
                            authorization.ExpiresAtUtc,
                            PreviousKeyThumbprintSha256 =
                                previousThumbprint,
                            UnlinkedAssetId = previousAssetId,
                            OldKeyRevoked = true,
                        }),
                    actor.IpAddress,
                    actor.Client,
                    actor.CorrelationId));
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CreatedAgentKeyRecovery(
                authorization.Id,
                agent.Id,
                authorization.ExpiresAtUtc,
                authorization.RequireTpm,
                token,
                authorization.Version);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AgentKeyRecoveryAuthorizationView>
        RevokeKeyRecoveryAsync(
            RevokeAgentKeyRecoveryCommand command,
            CancellationToken cancellationToken)
    {
        EnsureIdentifier(command.AgentId, "agentId");
        EnsureIdentifier(command.AuthorizationId, "authorizationId");
        EnsureVersion(command.Version);
        var actor = _currentUser.RequireActor();
        var authorization =
            await _repository.GetKeyRecoveryAuthorizationForUpdateAsync(
                    command.AuthorizationId,
                    cancellationToken)
                ?? throw new ResourceNotFoundException(
                    "Agent key-recovery authorization",
                    command.AuthorizationId);
        if (authorization.AgentId != command.AgentId)
        {
            throw new ResourceNotFoundException(
                "Agent key-recovery authorization",
                command.AuthorizationId);
        }

        try
        {
            authorization.Revoke(
                command.Version,
                actor.UserId,
                command.Reason,
                _clock.UtcNow);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainConflict(exception);
        }

        _repository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "agent.key_recovery.revoked",
                "managed_agent",
                command.AgentId.ToString(),
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        AgentId = command.AgentId,
                        AuthorizationId = authorization.Id,
                        command.Reason,
                    }),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        return ToView(authorization, _clock.UtcNow);
    }

    public async Task<ManagedAgentDetail> RevokeAsync(
        RevokeManagedAgentCommand command,
        CancellationToken cancellationToken)
    {
        EnsureIdentifier(command.AgentId, "agentId");
        EnsureVersion(command.Version);
        var actor = _currentUser.RequireActor();
        var agent = await _repository.GetAgentForUpdateAsync(
                command.AgentId,
                cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Managed agent",
                command.AgentId);
        var previousAssetId = agent.LinkedAssetId;
        try
        {
            agent.Revoke(command.Reason, command.Version, _clock.UtcNow);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainConflict(exception);
        }

        _repository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "agent.revoked",
                "managed_agent",
                agent.Id.ToString(),
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        agent.DeviceId,
                        command.Reason,
                        UnlinkedAssetId = previousAssetId,
                    }),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        return WithConnectivity(
            await _repository.GetDetailAsync(
                    agent.Id,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "The revoked agent could not be reloaded."));
    }

    private async Task<AgentEnrollmentResult> RecoverAgentKeyAsync(
        EnrollAgentCommand command,
        Guid authorizationId,
        string normalizedDeviceId,
        string normalizedThumbprint,
        DateTimeOffset now,
        IAgentTransaction transaction,
        CancellationToken cancellationToken)
    {
        var authorization =
            await _repository.GetKeyRecoveryAuthorizationForUpdateAsync(
                authorizationId,
                cancellationToken);
        if (authorization is null ||
            !FixedTimeEquals(
                authorization.TokenHash,
                HashEnrollmentToken(command.EnrollmentToken)))
        {
            throw InvalidEnrollment();
        }

        if (authorization.RequireTpm)
        {
            throw TpmAttestationUnavailable();
        }

        var agent = await _repository.GetAgentForUpdateAsync(
            authorization.AgentId,
            cancellationToken);
        if (agent is null ||
            !string.Equals(
                agent.DeviceIdNormalized,
                normalizedDeviceId,
                StringComparison.Ordinal))
        {
            throw InvalidEnrollment();
        }

        await ReserveNonceAsync(
            AgentRequestNonceScope.KeyRecovery,
            authorization.Id,
            command.Nonce,
            now,
            cancellationToken);
        var previousThumbprint = agent.KeyThumbprintSha256;
        try
        {
            agent.ReplaceKey(
                NormalizePem(command.PublicKeyPem),
                normalizedThumbprint,
                command.KeyProtection,
                command.Identity.Hostname,
                command.Identity.SerialNumber,
                command.Identity.BiosUuid,
                command.Identity.Manufacturer,
                command.Identity.Model,
                authorization.ExpectedAgentVersion,
                now);
            authorization.Consume(
                command.KeyProtection,
                normalizedThumbprint,
                now);
        }
        catch (DomainRuleException)
        {
            throw InvalidEnrollment();
        }

        _repository.AddAuditLog(
            AuditLog.Create(
                null,
                "Endpoint agent",
                "agent.key_recovered",
                "managed_agent",
                agent.Id.ToString(),
                now,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        AgentId = agent.Id,
                        AuthorizationId = authorization.Id,
                        agent.DeviceId,
                        PreviousKeyThumbprintSha256 = previousThumbprint,
                        ReplacementKeyThumbprintSha256 =
                            agent.KeyThumbprintSha256,
                        agent.KeyProtection,
                        ApprovedByUserId =
                            authorization.CreatedByUserId,
                        ApprovedByDisplayName =
                            authorization.CreatedByDisplayName,
                    }),
                command.IpAddress,
                command.Client,
                command.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AgentEnrollmentResult(
            agent.Id,
            agent.DeviceId,
            now,
            agent.LastSequence + 1,
            _settings.CheckInIntervalSeconds);
    }

    private void ValidateSnapshot(
        AgentDeviceSnapshot snapshot,
        DateTimeOffset now)
    {
        EnsureUtc(snapshot.CollectedAtUtc, "snapshot.collectedAtUtc");
        if (snapshot.SnapshotId == Guid.Empty)
        {
            throw new RequestValidationException(
                "agent_snapshot_id_required",
                "A snapshot identifier is required.");
        }

        if (snapshot.CollectedAtUtc > now.AddMinutes(10) ||
            snapshot.CollectedAtUtc <
            now.AddDays(-_settings.MaximumOfflineSnapshotDays))
        {
            throw new RequestValidationException(
                "agent_snapshot_time_invalid",
                "The snapshot collection time is outside the accepted window.");
        }

        if (snapshot.NetworkAdapters.Count > AgentProtocolMaximumNetworkAdapters ||
            snapshot.StorageVolumes.Count > AgentProtocolMaximumStorageVolumes ||
            snapshot.InstalledSoftware.Count >
            AgentProtocolMaximumInstalledSoftwareItems)
        {
            throw new RequestValidationException(
                "agent_snapshot_collection_limit",
                "The snapshot contains too many collection items.");
        }

        if (snapshot.NetworkAdapters.Any(
                adapter => adapter.Ipv4Addresses.Count > 32))
        {
            throw new RequestValidationException(
                "agent_snapshot_network_limit",
                "A network adapter contains too many addresses.");
        }
    }

    private static void ValidatePublicKey(
        string publicKeyPem,
        string expectedThumbprint,
        string signature,
        string canonical)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(NormalizePem(publicKeyPem));
            if (ecdsa.KeySize != 256)
            {
                throw InvalidEnrollment();
            }

            var thumbprint = Convert.ToHexString(
                    SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo()))
                .ToLowerInvariant();
            if (!FixedTimeEquals(thumbprint, expectedThumbprint))
            {
                throw InvalidEnrollment();
            }

            VerifySignature(ecdsa, canonical, signature);
        }
        catch (RequestAuthenticationException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is CryptographicException or FormatException or ArgumentException)
        {
            throw InvalidEnrollment();
        }
    }

    private static void VerifySignature(
        string publicKeyPem,
        string canonical,
        string signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(NormalizePem(publicKeyPem));
            VerifySignature(ecdsa, canonical, signature);
        }
        catch (RequestAuthenticationException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is CryptographicException or FormatException or ArgumentException)
        {
            throw InvalidAgentAuthentication();
        }
    }

    private static void VerifySignature(
        ECDsa ecdsa,
        string canonical,
        string signature)
    {
        var signatureBytes = Convert.FromBase64String(signature);
        if (signatureBytes.Length != 64 ||
            !ecdsa.VerifyData(
                Encoding.UTF8.GetBytes(canonical),
                signatureBytes,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            throw InvalidAgentAuthentication();
        }
    }

    private static string BuildEnrollmentCanonical(
        EnrollAgentCommand command) =>
        string.Join(
            '\n',
            "INVENTORY-AGENT-ENROLL-V1",
            command.TimestampEpochSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            Encode(command.Nonce),
            Encode(HashEnrollmentToken(command.EnrollmentToken)),
            Encode(command.DeviceId),
            Encode(command.KeyThumbprintSha256.ToLowerInvariant()),
            Encode(command.SignatureAlgorithm.ToUpperInvariant()),
            Encode(command.KeyProtection.ToString()),
            Encode(HashIdentity(command.Identity)));

    private static string BuildCheckInCanonical(
        CheckInAgentCommand command) =>
        string.Join(
            '\n',
            "INVENTORY-AGENT-CHECKIN-V1",
            "POST",
            "/api/v1/agent/check-ins",
            command.AgentId.ToString("D"),
            Encode(command.DeviceId),
            command.Snapshot.SnapshotId.ToString("D"),
            command.Sequence.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            command.TimestampEpochSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            Encode(command.Nonce),
            Encode(command.BodySha256));

    private static byte[] SerializeSnapshot(AgentDeviceSnapshot snapshot) =>
        JsonSerializer.SerializeToUtf8Bytes(snapshot, SnapshotJsonOptions);

    private static string HashSnapshotContent(AgentDeviceSnapshot snapshot) =>
        Convert.ToBase64String(
            SHA256.HashData(
                SerializeSnapshot(
                    snapshot with
                    {
                        SnapshotId = Guid.Empty,
                        CollectedAtUtc = DateTimeOffset.UnixEpoch,
                    })));

    private static string HashIdentity(AgentIdentity identity) =>
        Convert.ToBase64String(
            SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(
                    identity,
                    SnapshotJsonOptions)));

    private static string HashEnrollmentToken(string token) =>
        Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));

    private static string HashNonce(string nonce) =>
        Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(nonce.Trim())));

    private static AgentEnrollmentTokenReference ParseEnrollmentToken(
        string token)
    {
        var parts = token.Trim().Split('.', StringSplitOptions.None);
        if (parts.Length != 3 ||
            parts[2].Length is < 40 or > 50 ||
            !Guid.TryParseExact(parts[1], "N", out var tokenId))
        {
            throw InvalidEnrollment();
        }

        var kind = parts[0] switch
        {
            TokenPrefix => AgentEnrollmentTokenKind.Profile,
            KeyRecoveryTokenPrefix => AgentEnrollmentTokenKind.KeyRecovery,
            _ => throw InvalidEnrollment(),
        };
        return new AgentEnrollmentTokenReference(kind, tokenId);
    }

    private static void ValidateTimestamp(
        long timestampEpochSeconds,
        DateTimeOffset now)
    {
        DateTimeOffset timestamp;
        try
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(
                timestampEpochSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw InvalidAgentAuthentication();
        }

        if ((timestamp - now).Duration() >
            TimeSpan.FromSeconds(AgentProtocolTimestampWindowSeconds))
        {
            throw new RequestAuthenticationException(
                "agent_timestamp_invalid",
                "The endpoint request timestamp is outside the accepted window.");
        }
    }

    private static void ValidateNonce(string nonce)
    {
        try
        {
            var bytes = Convert.FromBase64String(nonce);
            if (bytes.Length is < 16 or > 64)
            {
                throw InvalidAgentAuthentication();
            }
        }
        catch (FormatException)
        {
            throw InvalidAgentAuthentication();
        }
    }

    private async Task ReserveNonceAsync(
        AgentRequestNonceScope scope,
        Guid contextId,
        string nonce,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _repository.DeleteExpiredRequestNoncesAsync(
            now,
            cancellationToken);
        var reservation = AgentRequestNonce.Create(
            scope,
            contextId,
            HashNonce(nonce),
            now,
            now.AddSeconds(AgentProtocolTimestampWindowSeconds * 2));
        if (!await _repository.TryReserveRequestNonceAsync(
                reservation,
                cancellationToken))
        {
            throw new RequestAuthenticationException(
                "agent_nonce_replayed",
                "The endpoint request nonce has already been used.");
        }
    }

    private static void EnsureSupportedAlgorithm(string algorithm)
    {
        if (!string.Equals(
                algorithm,
                "ECDSA_P256_SHA256",
                StringComparison.Ordinal))
        {
            throw InvalidEnrollment();
        }
    }

    private static string NormalizeThumbprint(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 ||
            normalized.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw InvalidEnrollment();
        }

        return normalized;
    }

    private static string NormalizePem(string value) =>
        value.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string NormalizeKey(string value) =>
        value.Trim().ToUpperInvariant();

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(
                   leftBytes,
                   rightBytes);
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value.Trim()));

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static void EnsureUtc(DateTimeOffset value, string field)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new RequestValidationException(
                "utc_required",
                $"{field} must be UTC.");
        }
    }

    private (
        DateTimeOffset Now,
        DateTimeOffset OnlineCutoff,
        DateTimeOffset StaleCutoff)
        GetConnectivityBoundaries()
    {
        var now = _clock.UtcNow;
        return (
            now,
            now.AddSeconds(
                -(_settings.CheckInIntervalSeconds +
                  _settings.ConnectivityGraceSeconds)),
            now.AddSeconds(
                -_settings.ConnectivityOfflineAfterSeconds));
    }

    private ManagedAgentDetail WithConnectivity(
        ManagedAgentDetail detail) =>
        detail with
        {
            Agent = WithConnectivity(detail.Agent, _clock.UtcNow),
        };

    private ManagedAgentListItem WithConnectivity(
        ManagedAgentListItem agent,
        DateTimeOffset now)
    {
        if (!agent.LastCheckInAtUtc.HasValue)
        {
            return agent with
            {
                ConnectivityStatus =
                    ManagedAgentConnectivityStatus.NeverCheckedIn,
                NextExpectedCheckInAtUtc = null,
                OverdueAtUtc = null,
            };
        }

        var nextExpected = agent.LastCheckInAtUtc.Value.AddSeconds(
            _settings.CheckInIntervalSeconds);
        var overdueAt = nextExpected.AddSeconds(
            _settings.ConnectivityGraceSeconds);
        var offlineAt = agent.LastCheckInAtUtc.Value.AddSeconds(
            _settings.ConnectivityOfflineAfterSeconds);
        var status = now <= overdueAt
            ? ManagedAgentConnectivityStatus.Online
            : now <= offlineAt
                ? ManagedAgentConnectivityStatus.Stale
                : ManagedAgentConnectivityStatus.Offline;
        return agent with
        {
            ConnectivityStatus = status,
            NextExpectedCheckInAtUtc = nextExpected,
            OverdueAtUtc = overdueAt,
        };
    }

    private async Task ValidateAssetReferencesAsync(
        Guid categoryId,
        Guid? locationId,
        CancellationToken cancellationToken)
    {
        var category = await _inventoryRepository.GetCategoryAsync(
            categoryId,
            cancellationToken);
        if (category is null || !category.IsActive)
        {
            throw new RequestValidationException(
                "invalid_category",
                "The selected category does not exist or is inactive.",
                new Dictionary<string, string[]>
                {
                    ["categoryId"] =
                    [
                        "The selected category does not exist or is inactive.",
                    ],
                });
        }

        if (locationId.HasValue)
        {
            var location = await _inventoryRepository.GetLocationAsync(
                locationId.Value,
                cancellationToken);
            if (location is null || !location.IsActive)
            {
                throw new RequestValidationException(
                    "invalid_location",
                    "The selected location does not exist or is inactive.",
                    new Dictionary<string, string[]>
                    {
                        ["locationId"] =
                        [
                            "The selected location does not exist or is inactive.",
                        ],
                    });
            }
        }
    }

    private async Task EnsureAssetIdentifiersAvailableAsync(
        string assetTag,
        string? serialNumber,
        CancellationToken cancellationToken)
    {
        var normalizedTag = NormalizeRequiredKey(assetTag, "assetTag");
        if (await _inventoryRepository.AssetTagExistsAsync(
                normalizedTag,
                excludingAssetId: null,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "duplicate_asset_tag",
                $"Asset tag '{assetTag.Trim()}' is already in use.");
        }

        var normalizedSerial = string.IsNullOrWhiteSpace(serialNumber)
            ? null
            : serialNumber.Trim().ToUpperInvariant();
        if (normalizedSerial is not null &&
            await _inventoryRepository.SerialNumberExistsAsync(
                normalizedSerial,
                excludingAssetId: null,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "duplicate_serial_number",
                $"Serial number '{serialNumber!.Trim()}' is already in use.");
        }
    }

    private static AssetDetails ToAssetDetails(
        CreateAssetCommand command) =>
        new(
            command.AssetTag
                ?? throw new InvalidOperationException(
                    "The asset tag was not resolved before asset creation."),
            command.CategoryId,
            command.Manufacturer,
            command.Model,
            command.SerialNumber,
            command.Status,
            command.Condition,
            command.LocationId,
            command.PurchaseDate,
            command.PurchaseCost,
            command.Currency,
            command.Supplier,
            command.Notes,
            command.LegacyId,
            command.DeviceName,
            command.Description,
            command.LanMacAddress,
            command.BiosGuid,
            command.Ownership,
            command.OperatingSystem,
            command.OperatingSystemVersion,
            command.Windows11Compliant);

    private int GetCheckInRetryAfterSeconds(
        ManagedAgent agent,
        string code,
        DateTimeOffset now)
    {
        var (windowStart, window) =
            code == "agent_check_in_burst_exceeded"
                ? (
                    agent.CheckInBurstWindowStartedAtUtc,
                    TimeSpan.FromSeconds(
                        _settings.CheckInBurstWindowSeconds))
                : (
                    agent.CheckInQuotaWindowStartedAtUtc,
                    TimeSpan.FromHours(
                        _settings.CheckInQuotaWindowHours));
        var remaining = windowStart.HasValue
            ? window - (now - windowStart.Value)
            : window;
        return Math.Max(
            1,
            (int)Math.Ceiling(
                Math.Min(remaining.TotalSeconds, int.MaxValue)));
    }

    private static void EnsureIdentifier(Guid value, string field)
    {
        if (value == Guid.Empty)
        {
            throw new RequestValidationException(
                "identifier_required",
                $"{field} must be a non-empty GUID.",
                new Dictionary<string, string[]>
                {
                    [field] = [$"{field} must be a non-empty GUID."],
                });
        }
    }

    private static void EnsureVersion(Guid version)
    {
        if (version == Guid.Empty)
        {
            throw new RequestValidationException(
                "version_required",
                "version must be a non-empty GUID.",
                new Dictionary<string, string[]>
                {
                    ["version"] = ["version must be a non-empty GUID."],
                });
        }
    }

    private static string NormalizeRequiredKey(
        string? value,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new RequestValidationException(
                "required",
                $"{field} is required.",
                new Dictionary<string, string[]>
                {
                    [field] = [$"{field} is required."],
                });
        }

        return value.Trim().ToUpperInvariant();
    }

    private async Task SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new ResourceConflictException(
                exception.Code,
                exception.Message,
                exception);
        }
    }

    private static ResourceConflictException MapDomainConflict(
        DomainRuleException exception) =>
        new(exception.Code, exception.Message, exception);

    private static void ValidatePaging(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 200)
        {
            throw new RequestValidationException(
                "invalid_paging",
                "Page must be positive and pageSize must be between 1 and 200.");
        }
    }

    private static RequestAuthenticationException InvalidEnrollment() =>
        new(
            "agent_enrollment_denied",
            "The endpoint enrollment request was not accepted.");

    private static ResourceConflictException TpmAttestationUnavailable() =>
        new(
            "agent_tpm_attestation_unavailable",
            "Verified TPM attestation is not configured; a reported TPM key cannot satisfy RequireTpm.");

    private static RequestAuthenticationException InvalidAgentAuthentication() =>
        new(
            "agent_authentication_failed",
            "The endpoint agent request was not accepted.");

    private static AgentEnrollmentProfileView ToView(
        AgentEnrollmentProfile profile) =>
        new(
            profile.Id,
            profile.Name,
            profile.ExpiresAtUtc,
            profile.MaxEnrollments,
            profile.EnrolledCount,
            profile.RequireTpm,
            profile.AllowedHostnameSuffix,
            profile.CreatedAtUtc,
            profile.CreatedByUserId,
            profile.RevokedAtUtc,
            profile.Version);

    private static AgentKeyRecoveryAuthorizationView ToView(
        AgentKeyRecoveryAuthorization authorization,
        DateTimeOffset now) =>
        new(
            authorization.Id,
            authorization.AgentId,
            GetStatus(authorization, now),
            authorization.Reason,
            authorization.CreatedAtUtc,
            authorization.CreatedByUserId,
            authorization.CreatedByDisplayName,
            authorization.ExpiresAtUtc,
            authorization.RequireTpm,
            authorization.ConsumedAtUtc,
            authorization.RevokedAtUtc,
            authorization.RevokedByUserId,
            authorization.RevokedReason,
            authorization.Version);

    private static AgentKeyRecoveryAuthorizationStatus GetStatus(
        AgentKeyRecoveryAuthorization authorization,
        DateTimeOffset now)
    {
        if (authorization.ConsumedAtUtc.HasValue)
        {
            return AgentKeyRecoveryAuthorizationStatus.Consumed;
        }

        if (authorization.RevokedAtUtc.HasValue)
        {
            return AgentKeyRecoveryAuthorizationStatus.Revoked;
        }

        return authorization.ExpiresAtUtc <= now
            ? AgentKeyRecoveryAuthorizationStatus.Expired
            : AgentKeyRecoveryAuthorizationStatus.Pending;
    }

    private static JsonSerializerOptions CreateSnapshotJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = false,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));
        return options;
    }

    private const int AgentProtocolTimestampWindowSeconds = 300;
    private const int AgentProtocolMaximumSnapshotBytes = 1_048_576;
    private const int AgentProtocolMaximumInstalledSoftwareItems = 5000;
    private const int AgentProtocolMaximumNetworkAdapters = 64;
    private const int AgentProtocolMaximumStorageVolumes = 64;

    private enum AgentEnrollmentTokenKind
    {
        Profile,
        KeyRecovery,
    }

    private sealed record AgentEnrollmentTokenReference(
        AgentEnrollmentTokenKind Kind,
        Guid Id);
}
