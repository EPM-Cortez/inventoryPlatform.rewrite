using Inventory.Domain.Common;

namespace Inventory.Domain.Agents;

public sealed class ManagedAgent
{
    private ManagedAgent()
    {
    }

    private ManagedAgent(
        Guid enrollmentProfileId,
        string deviceId,
        string publicKeyPem,
        string keyThumbprintSha256,
        AgentKeyProtection keyProtection,
        string hostname,
        string? serialNumber,
        string? biosUuid,
        string? manufacturer,
        string? model,
        DateTimeOffset enrolledAtUtc)
    {
        if (enrollmentProfileId == Guid.Empty)
        {
            throw new DomainRuleException(
                "agent_enrollment_profile_required",
                "An enrollment profile is required.");
        }

        ValidateUtc(enrolledAtUtc, nameof(enrolledAtUtc));
        Id = Guid.NewGuid();
        EnrollmentProfileId = enrollmentProfileId;
        DeviceId = TextRules.Required(deviceId, nameof(DeviceId), 100);
        DeviceIdNormalized = TextRules.NormalizeKey(DeviceId);
        PublicKeyPem = TextRules.Required(publicKeyPem, nameof(PublicKeyPem), 2000);
        KeyThumbprintSha256 = TextRules.Required(
                keyThumbprintSha256,
                nameof(KeyThumbprintSha256),
                64)
            .ToLowerInvariant();
        KeyProtection = keyProtection;
        Hostname = TextRules.Required(hostname, nameof(Hostname), 150);
        HostnameNormalized = TextRules.NormalizeKey(Hostname);
        SerialNumber = TextRules.Optional(
            serialNumber,
            nameof(SerialNumber),
            200);
        SerialNumberNormalized = SerialNumber is null
            ? null
            : TextRules.NormalizeKey(SerialNumber);
        BiosUuid = TextRules.Optional(biosUuid, nameof(BiosUuid), 100);
        BiosUuidNormalized = BiosUuid is null
            ? null
            : TextRules.NormalizeKey(BiosUuid);
        Manufacturer = TextRules.Optional(manufacturer, nameof(Manufacturer), 150);
        Model = TextRules.Optional(model, nameof(Model), 150);
        EnrolledAtUtc = enrolledAtUtc;
        MatchState = ManagedAgentMatchState.Unmatched;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid EnrollmentProfileId { get; private set; }

    public Guid? LinkedAssetId { get; private set; }

    public string DeviceId { get; private set; } = string.Empty;

    public string DeviceIdNormalized { get; private set; } = string.Empty;

    public string PublicKeyPem { get; private set; } = string.Empty;

    public string KeyThumbprintSha256 { get; private set; } = string.Empty;

    public AgentKeyProtection KeyProtection { get; private set; }

    public string Hostname { get; private set; } = string.Empty;

    public string HostnameNormalized { get; private set; } = string.Empty;

    public string? SerialNumber { get; private set; }

    public string? SerialNumberNormalized { get; private set; }

    public string? BiosUuid { get; private set; }

    public string? BiosUuidNormalized { get; private set; }

    public string? Manufacturer { get; private set; }

    public string? Model { get; private set; }

    public ManagedAgentMatchState MatchState { get; private set; }

    public string? MatchReason { get; private set; }

    public DateTimeOffset EnrolledAtUtc { get; private set; }

    public DateTimeOffset? LastCheckInAtUtc { get; private set; }

    public DateTimeOffset? LastCollectedAtUtc { get; private set; }

    public string? LastAgentVersion { get; private set; }

    public string? LastSeenIpAddress { get; private set; }

    public Guid? LastSnapshotId { get; private set; }

    public long LastSequence { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public string? RevokedReason { get; private set; }

    public DateTimeOffset? KeyReplacedAtUtc { get; private set; }

    public int KeyReplacementCount { get; private set; }

    public DateTimeOffset? CheckInBurstWindowStartedAtUtc { get; private set; }

    public int CheckInBurstCount { get; private set; }

    public DateTimeOffset? CheckInQuotaWindowStartedAtUtc { get; private set; }

    public int CheckInQuotaCount { get; private set; }

    public Guid Version { get; private set; }

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public static ManagedAgent Enroll(
        Guid enrollmentProfileId,
        string deviceId,
        string publicKeyPem,
        string keyThumbprintSha256,
        AgentKeyProtection keyProtection,
        string hostname,
        string? serialNumber,
        string? biosUuid,
        string? manufacturer,
        string? model,
        DateTimeOffset enrolledAtUtc) =>
        new(
            enrollmentProfileId,
            deviceId,
            publicKeyPem,
            keyThumbprintSha256,
            keyProtection,
            hostname,
            serialNumber,
            biosUuid,
            manufacturer,
            model,
            enrolledAtUtc);

    public void RecordCheckIn(
        long sequence,
        Guid snapshotId,
        DateTimeOffset collectedAtUtc,
        DateTimeOffset receivedAtUtc,
        string agentVersion,
        string hostname,
        string? serialNumber,
        string? biosUuid,
        string? manufacturer,
        string? model,
        string? seenIpAddress)
    {
        RecordCheckIn(
            sequence,
            snapshotId,
            collectedAtUtc,
            receivedAtUtc,
            agentVersion,
            hostname,
            serialNumber,
            biosUuid,
            manufacturer,
            model,
            seenIpAddress,
            TimeSpan.FromHours(1),
            1_000_000,
            TimeSpan.FromDays(1),
            1_000_000);
    }

    public void RecordCheckIn(
        long sequence,
        Guid snapshotId,
        DateTimeOffset collectedAtUtc,
        DateTimeOffset receivedAtUtc,
        string agentVersion,
        string hostname,
        string? serialNumber,
        string? biosUuid,
        string? manufacturer,
        string? model,
        string? seenIpAddress,
        TimeSpan burstWindow,
        int burstLimit,
        TimeSpan quotaWindow,
        int quotaLimit)
    {
        ValidateUtc(collectedAtUtc, nameof(collectedAtUtc));
        ValidateUtc(receivedAtUtc, nameof(receivedAtUtc));
        if (IsRevoked)
        {
            throw new DomainRuleException(
                "agent_revoked",
                "The endpoint agent has been revoked.");
        }

        if (sequence <= LastSequence)
        {
            throw new DomainRuleException(
                "agent_sequence_replayed",
                "The endpoint agent sequence must increase.");
        }

        if (snapshotId == Guid.Empty)
        {
            throw new DomainRuleException(
                "agent_snapshot_id_required",
                "The endpoint snapshot identifier is required.");
        }

        ConsumeCheckInCapacity(
            receivedAtUtc,
            burstWindow,
            burstLimit,
            quotaWindow,
            quotaLimit);
        LastSequence = sequence;
        LastSnapshotId = snapshotId;
        LastCollectedAtUtc = collectedAtUtc;
        LastCheckInAtUtc = receivedAtUtc;
        LastAgentVersion = TextRules.Required(
            agentVersion,
            nameof(LastAgentVersion),
            50);
        Hostname = TextRules.Required(hostname, nameof(Hostname), 150);
        HostnameNormalized = TextRules.NormalizeKey(Hostname);
        SerialNumber = TextRules.Optional(
                serialNumber,
                nameof(SerialNumber),
                200)
            ?? SerialNumber;
        SerialNumberNormalized = SerialNumber is null
            ? null
            : TextRules.NormalizeKey(SerialNumber);
        BiosUuid = TextRules.Optional(biosUuid, nameof(BiosUuid), 100)
            ?? BiosUuid;
        BiosUuidNormalized = BiosUuid is null
            ? null
            : TextRules.NormalizeKey(BiosUuid);
        Manufacturer = TextRules.Optional(
                manufacturer,
                nameof(Manufacturer),
                150)
            ?? Manufacturer;
        Model = TextRules.Optional(model, nameof(Model), 150) ?? Model;
        LastSeenIpAddress = TextRules.Optional(
            seenIpAddress,
            nameof(LastSeenIpAddress),
            100);
        Version = Guid.NewGuid();
    }

    public void AutoLinkAsset(Guid assetId, string reason)
    {
        if (LinkedAssetId.HasValue ||
            MatchState != ManagedAgentMatchState.Unmatched)
        {
            return;
        }

        LinkAssetCore(assetId, reason);
    }

    public void LinkAsset(Guid assetId, string? reason, Guid expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (IsRevoked)
        {
            throw new DomainRuleException(
                "agent_revoked",
                "A revoked endpoint agent cannot be linked.");
        }

        LinkAssetCore(assetId, reason ?? "Linked by administrator");
    }

    public void MarkMatchConflict(string reason)
    {
        if (LinkedAssetId.HasValue ||
            MatchState == ManagedAgentMatchState.Dismissed)
        {
            return;
        }

        var normalizedReason = TextRules.Required(
            reason,
            nameof(MatchReason),
            500);
        if (MatchState == ManagedAgentMatchState.Conflict &&
            string.Equals(
                MatchReason,
                normalizedReason,
                StringComparison.Ordinal))
        {
            return;
        }

        MatchState = ManagedAgentMatchState.Conflict;
        MatchReason = normalizedReason;
        Version = Guid.NewGuid();
    }

    public void UnlinkAsset(string reason, Guid expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (!LinkedAssetId.HasValue)
        {
            throw new DomainRuleException(
                "agent_not_linked",
                "The endpoint agent is not linked to an asset.");
        }

        LinkedAssetId = null;
        MatchState = ManagedAgentMatchState.Conflict;
        MatchReason = TextRules.Required(reason, nameof(MatchReason), 500);
        Version = Guid.NewGuid();
    }

    public void DismissMatch(string reason, Guid expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (IsRevoked)
        {
            throw new DomainRuleException(
                "agent_revoked",
                "A revoked endpoint agent cannot be dismissed.");
        }

        if (LinkedAssetId.HasValue)
        {
            throw new DomainRuleException(
                "agent_is_linked",
                "A linked endpoint agent cannot be dismissed.");
        }

        if (MatchState == ManagedAgentMatchState.Dismissed)
        {
            throw new DomainRuleException(
                "agent_already_dismissed",
                "The endpoint agent is already dismissed.");
        }

        MatchState = ManagedAgentMatchState.Dismissed;
        MatchReason = TextRules.Required(reason, nameof(MatchReason), 500);
        Version = Guid.NewGuid();
    }

    public void RestoreMatchReview(string reason, Guid expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (IsRevoked)
        {
            throw new DomainRuleException(
                "agent_revoked",
                "A revoked endpoint agent cannot be restored for review.");
        }

        if (MatchState is not (
                ManagedAgentMatchState.Dismissed or
                ManagedAgentMatchState.Conflict))
        {
            throw new DomainRuleException(
                "agent_not_awaiting_review",
                "Only a dismissed or conflicting endpoint agent can be restored for review.");
        }

        MatchState = ManagedAgentMatchState.Unmatched;
        MatchReason = TextRules.Required(reason, nameof(MatchReason), 500);
        Version = Guid.NewGuid();
    }

    public void Revoke(
        string reason,
        Guid expectedVersion,
        DateTimeOffset revokedAtUtc)
    {
        EnsureVersion(expectedVersion);
        ValidateUtc(revokedAtUtc, nameof(revokedAtUtc));
        if (IsRevoked)
        {
            throw new DomainRuleException(
                "agent_already_revoked",
                "The endpoint agent is already revoked.");
        }

        RevokedReason = TextRules.Required(reason, nameof(RevokedReason), 500);
        RevokedAtUtc = revokedAtUtc;
        RemoveAssetLinkAfterTrustRevocation();
        Version = Guid.NewGuid();
    }

    public void ApproveKeyRecovery(
        string reason,
        Guid expectedVersion,
        DateTimeOffset approvedAtUtc)
    {
        EnsureVersion(expectedVersion);
        ValidateUtc(approvedAtUtc, nameof(approvedAtUtc));
        _ = TextRules.Required(reason, nameof(reason), 500);
        if (!IsRevoked)
        {
            RevokedReason =
                "Key recovery approved by an administrator.";
            RevokedAtUtc = approvedAtUtc;
        }

        RemoveAssetLinkAfterTrustRevocation();
        Version = Guid.NewGuid();
    }

    public void ReplaceKey(
        string publicKeyPem,
        string keyThumbprintSha256,
        AgentKeyProtection keyProtection,
        string hostname,
        string? serialNumber,
        string? biosUuid,
        string? manufacturer,
        string? model,
        Guid expectedVersion,
        DateTimeOffset replacedAtUtc)
    {
        EnsureVersion(expectedVersion);
        ValidateUtc(replacedAtUtc, nameof(replacedAtUtc));
        if (!IsRevoked)
        {
            throw new DomainRuleException(
                "agent_key_recovery_not_approved",
                "The endpoint key replacement has not been approved.");
        }

        var normalizedThumbprint = TextRules.Required(
                keyThumbprintSha256,
                nameof(KeyThumbprintSha256),
                64)
            .ToLowerInvariant();
        if (string.Equals(
                normalizedThumbprint,
                KeyThumbprintSha256,
                StringComparison.Ordinal))
        {
            throw new DomainRuleException(
                "agent_recovery_key_unchanged",
                "The replacement key must differ from the revoked key.");
        }

        PublicKeyPem = TextRules.Required(
            publicKeyPem,
            nameof(PublicKeyPem),
            2000);
        KeyThumbprintSha256 = normalizedThumbprint;
        KeyProtection = keyProtection;
        Hostname = TextRules.Required(hostname, nameof(Hostname), 150);
        HostnameNormalized = TextRules.NormalizeKey(Hostname);
        SerialNumber = TextRules.Optional(
            serialNumber,
            nameof(SerialNumber),
            200);
        SerialNumberNormalized = SerialNumber is null
            ? null
            : TextRules.NormalizeKey(SerialNumber);
        BiosUuid = TextRules.Optional(biosUuid, nameof(BiosUuid), 100);
        BiosUuidNormalized = BiosUuid is null
            ? null
            : TextRules.NormalizeKey(BiosUuid);
        Manufacturer = TextRules.Optional(
            manufacturer,
            nameof(Manufacturer),
            150);
        Model = TextRules.Optional(model, nameof(Model), 150);
        RevokedAtUtc = null;
        RevokedReason = null;
        KeyReplacedAtUtc = replacedAtUtc;
        KeyReplacementCount++;
        LinkedAssetId = null;
        MatchState = ManagedAgentMatchState.Conflict;
        MatchReason =
            "Endpoint key recovered; confirm the asset link before relinking.";
        CheckInBurstWindowStartedAtUtc = null;
        CheckInBurstCount = 0;
        CheckInQuotaWindowStartedAtUtc = null;
        CheckInQuotaCount = 0;
        Version = Guid.NewGuid();
    }

    private void LinkAssetCore(Guid assetId, string reason)
    {
        if (assetId == Guid.Empty)
        {
            throw new DomainRuleException(
                "agent_asset_required",
                "A valid asset identifier is required.");
        }

        LinkedAssetId = assetId;
        MatchState = ManagedAgentMatchState.Matched;
        MatchReason = TextRules.Required(reason, nameof(MatchReason), 500);
        Version = Guid.NewGuid();
    }

    private void ConsumeCheckInCapacity(
        DateTimeOffset receivedAtUtc,
        TimeSpan burstWindow,
        int burstLimit,
        TimeSpan quotaWindow,
        int quotaLimit)
    {
        if (burstWindow <= TimeSpan.Zero ||
            quotaWindow <= TimeSpan.Zero ||
            burstLimit < 1 ||
            quotaLimit < burstLimit)
        {
            throw new DomainRuleException(
                "agent_check_in_quota_invalid",
                "The endpoint check-in quota configuration is invalid.");
        }

        if (!CheckInBurstWindowStartedAtUtc.HasValue ||
            receivedAtUtc < CheckInBurstWindowStartedAtUtc.Value ||
            receivedAtUtc - CheckInBurstWindowStartedAtUtc.Value >= burstWindow)
        {
            CheckInBurstWindowStartedAtUtc = receivedAtUtc;
            CheckInBurstCount = 0;
        }

        if (!CheckInQuotaWindowStartedAtUtc.HasValue ||
            receivedAtUtc < CheckInQuotaWindowStartedAtUtc.Value ||
            receivedAtUtc - CheckInQuotaWindowStartedAtUtc.Value >= quotaWindow)
        {
            CheckInQuotaWindowStartedAtUtc = receivedAtUtc;
            CheckInQuotaCount = 0;
        }

        if (CheckInBurstCount >= burstLimit)
        {
            throw new DomainRuleException(
                "agent_check_in_burst_exceeded",
                "The endpoint agent submitted too many check-ins in a short period.");
        }

        if (CheckInQuotaCount >= quotaLimit)
        {
            throw new DomainRuleException(
                "agent_check_in_quota_exceeded",
                "The endpoint agent exceeded its accepted check-in quota.");
        }

        CheckInBurstCount++;
        CheckInQuotaCount++;
    }

    private void RemoveAssetLinkAfterTrustRevocation()
    {
        if (!LinkedAssetId.HasValue)
        {
            return;
        }

        LinkedAssetId = null;
        MatchState = ManagedAgentMatchState.Conflict;
        MatchReason =
            "Endpoint trust was revoked and its asset link was removed.";
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || expectedVersion != Version)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The endpoint agent was changed by another request.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                $"{name} must be UTC.");
        }
    }
}
