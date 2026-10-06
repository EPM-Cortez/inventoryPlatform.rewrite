using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Application.Abstractions;
using Inventory.Application.Agents;
using Inventory.Application.Common;
using Inventory.Domain.Agents;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Infrastructure.Data;

public sealed class EfAgentRepository : IAgentRepository
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions =
        CreateSnapshotJsonOptions();

    private readonly InventoryDbContext _dbContext;

    public EfAgentRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IAgentTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        return new EfAgentTransaction(transaction);
    }

    public Task<AgentEnrollmentProfile?> GetEnrollmentProfileForUpdateAsync(
        Guid profileId,
        CancellationToken cancellationToken) =>
        _dbContext.AgentEnrollmentProfiles.SingleOrDefaultAsync(
            profile => profile.Id == profileId,
            cancellationToken);

    public Task<ManagedAgent?> GetAgentByDeviceIdForUpdateAsync(
        string normalizedDeviceId,
        CancellationToken cancellationToken) =>
        _dbContext.ManagedAgents.SingleOrDefaultAsync(
            agent => agent.DeviceIdNormalized == normalizedDeviceId,
            cancellationToken);

    public Task<ManagedAgent?> GetAgentForUpdateAsync(
        Guid agentId,
        CancellationToken cancellationToken) =>
        _dbContext.ManagedAgents.SingleOrDefaultAsync(
            agent => agent.Id == agentId,
            cancellationToken);

    public Task<bool> AgentExistsAsync(
        Guid agentId,
        CancellationToken cancellationToken) =>
        _dbContext.ManagedAgents
            .AsNoTracking()
            .AnyAsync(agent => agent.Id == agentId, cancellationToken);

    public Task<AgentKeyRecoveryAuthorization?>
        GetKeyRecoveryAuthorizationForUpdateAsync(
            Guid authorizationId,
            CancellationToken cancellationToken) =>
        _dbContext.AgentKeyRecoveryAuthorizations.SingleOrDefaultAsync(
            authorization => authorization.Id == authorizationId,
            cancellationToken);

    public Task<AgentKeyRecoveryAuthorization?>
        GetPendingKeyRecoveryAuthorizationForUpdateAsync(
            Guid agentId,
            CancellationToken cancellationToken) =>
        _dbContext.AgentKeyRecoveryAuthorizations.SingleOrDefaultAsync(
            authorization =>
                authorization.AgentId == agentId &&
                authorization.ConsumedAtUtc == null &&
                authorization.RevokedAtUtc == null,
            cancellationToken);

    public Task<ManagedAgentSnapshot?> GetSnapshotAsync(
        Guid agentId,
        Guid snapshotId,
        CancellationToken cancellationToken) =>
        _dbContext.ManagedAgentSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(
                snapshot =>
                    snapshot.AgentId == agentId &&
                    snapshot.SnapshotId == snapshotId,
                cancellationToken);

    public Task<ManagedAgentSnapshot?> GetLatestDetailedSnapshotAsync(
        Guid agentId,
        CancellationToken cancellationToken) =>
        _dbContext.ManagedAgentSnapshots
            .AsNoTracking()
            .Where(
                snapshot =>
                    snapshot.AgentId == agentId &&
                    snapshot.PayloadJson != null)
            .OrderByDescending(snapshot => snapshot.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task DeleteExpiredRequestNoncesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _dbContext.AgentRequestNonces
            .Where(nonce => nonce.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<bool> TryReserveRequestNonceAsync(
        AgentRequestNonce nonce,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "AgentRequestNonces"
                 ("Id", "Scope", "ContextId", "NonceHash",
                  "ReceivedAtUtc", "ExpiresAtUtc")
             VALUES
                 ({nonce.Id}, {nonce.Scope.ToString()}, {nonce.ContextId},
                  {nonce.NonceHash}, {nonce.ReceivedAtUtc.UtcDateTime},
                  {nonce.ExpiresAtUtc.UtcDateTime})
             ON CONFLICT ("Scope", "ContextId", "NonceHash") DO NOTHING
             """,
            cancellationToken);
        return rows == 1;
    }

    public async Task<AgentAssetMatchResult> FindUnlinkedAssetMatchAsync(
        string? normalizedSerialNumber,
        Guid? biosGuid,
        CancellationToken cancellationToken)
    {
        if (normalizedSerialNumber is null && !biosGuid.HasValue)
        {
            return new AgentAssetMatchResult(null, HasConflict: false);
        }

        var candidates = await _dbContext.Assets
            .AsNoTracking()
            .Where(asset =>
                (normalizedSerialNumber != null &&
                 asset.SerialNumberNormalized == normalizedSerialNumber) ||
                (biosGuid.HasValue && asset.BiosGuid == biosGuid.Value))
            .Select(
                asset => new
                {
                    asset.Id,
                    IsTerminal =
                        asset.Status == AssetStatus.Retired ||
                        asset.Status == AssetStatus.Disposed,
                    IsLinked = _dbContext.ManagedAgents.Any(
                        agent => agent.LinkedAssetId == asset.Id),
                })
            .Take(2)
            .ToArrayAsync(cancellationToken);
        var single = candidates.Length == 1
            ? candidates[0]
            : null;
        return new AgentAssetMatchResult(
            single is { IsLinked: false, IsTerminal: false }
                ? single.Id
                : null,
            HasConflict:
                candidates.Length > 1 ||
                single is { IsLinked: true } ||
                single is { IsTerminal: true },
            HasTerminalMatch: candidates.Any(candidate => candidate.IsTerminal));
    }

    public Task<AgentAssetLinkTarget?> GetAssetLinkTargetAsync(
        Guid assetId,
        CancellationToken cancellationToken) =>
        _dbContext.Assets
            .AsNoTracking()
            .Where(asset => asset.Id == assetId)
            .Select(
                asset => new AgentAssetLinkTarget(
                    asset.Id,
                    asset.Status == AssetStatus.Retired ||
                    asset.Status == AssetStatus.Disposed))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> AssetLinkedToOtherAgentAsync(
        Guid assetId,
        Guid agentId,
        CancellationToken cancellationToken) =>
        _dbContext.ManagedAgents
            .AsNoTracking()
            .AnyAsync(
                agent =>
                    agent.LinkedAssetId == assetId &&
                    agent.Id != agentId,
                cancellationToken);

    public async Task<PageResult<ManagedAgentListItem>> SearchAsync(
        ManagedAgentSearch search,
        CancellationToken cancellationToken)
    {
        var query = BuildAgentListQuery(
            includeRevoked: search.IncludeRevoked,
            isRevoked: search.IsRevoked,
            matchState: search.MatchState,
            connectivity: search.Connectivity,
            normalizedQuery: search.Query,
            onlineCutoffUtc: search.OnlineCutoffUtc,
            staleCutoffUtc: search.StaleCutoffUtc,
            orderForList: true);
        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToArrayAsync(cancellationToken);
        return new PageResult<ManagedAgentListItem>(
            items,
            search.Page,
            search.PageSize,
            totalCount);
    }

    public async Task<ManagedAgentFleetSummary> GetFleetSummaryAsync(
        bool includeRevoked,
        DateTimeOffset onlineCutoffUtc,
        DateTimeOffset staleCutoffUtc,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.ManagedAgents.AsNoTracking();
        if (!includeRevoked)
        {
            query = query.Where(agent => agent.RevokedAtUtc == null);
        }

        var summary = await query
            .GroupBy(_ => 1)
            .Select(
                agents => new ManagedAgentFleetSummary(
                    agents.LongCount(),
                    agents.LongCount(agent => agent.RevokedAtUtc == null),
                    agents.LongCount(agent => agent.RevokedAtUtc != null),
                    agents.LongCount(
                        agent =>
                            agent.MatchState ==
                            ManagedAgentMatchState.Matched),
                    agents.LongCount(
                        agent =>
                            agent.MatchState ==
                            ManagedAgentMatchState.Unmatched),
                    agents.LongCount(
                        agent =>
                            agent.MatchState ==
                            ManagedAgentMatchState.Conflict),
                    agents.LongCount(
                        agent =>
                            agent.MatchState ==
                            ManagedAgentMatchState.Dismissed),
                    agents.LongCount(
                        agent =>
                            agent.LastCheckInAtUtc != null &&
                            agent.LastCheckInAtUtc >= onlineCutoffUtc),
                    agents.LongCount(
                        agent =>
                            agent.LastCheckInAtUtc != null &&
                            agent.LastCheckInAtUtc < onlineCutoffUtc &&
                            agent.LastCheckInAtUtc >= staleCutoffUtc),
                    agents.LongCount(
                        agent =>
                            agent.LastCheckInAtUtc != null &&
                            agent.LastCheckInAtUtc < staleCutoffUtc),
                    agents.LongCount(
                        agent => agent.LastCheckInAtUtc == null)))
            .SingleOrDefaultAsync(cancellationToken);
        return summary
            ?? new ManagedAgentFleetSummary(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
    }

    public async Task<PageResult<AgentEnrollmentProfileView>>
        SearchEnrollmentProfilesAsync(
            bool includeRevoked,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
    {
        var query = _dbContext.AgentEnrollmentProfiles.AsNoTracking();
        if (!includeRevoked)
        {
            query = query.Where(profile => profile.RevokedAtUtc == null);
        }

        var ordered = query
            .OrderByDescending(profile => profile.CreatedAtUtc)
            .ThenBy(profile => profile.Name);
        var totalCount = await ordered.LongCountAsync(cancellationToken);
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(profile => new AgentEnrollmentProfileView(
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
                profile.Version))
            .ToArrayAsync(cancellationToken);
        return new PageResult<AgentEnrollmentProfileView>(
            items,
            page,
            pageSize,
            totalCount);
    }

    public async Task<PageResult<AgentKeyRecoveryAuthorization>>
        SearchKeyRecoveryAuthorizationsAsync(
            Guid agentId,
            bool includeClosed,
            DateTimeOffset now,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
    {
        var query = _dbContext.AgentKeyRecoveryAuthorizations
            .AsNoTracking()
            .Where(authorization => authorization.AgentId == agentId);
        if (!includeClosed)
        {
            query = query.Where(
                authorization =>
                    authorization.ConsumedAtUtc == null &&
                    authorization.RevokedAtUtc == null &&
                    authorization.ExpiresAtUtc > now);
        }

        var ordered = query
            .OrderByDescending(authorization => authorization.CreatedAtUtc)
            .ThenByDescending(authorization => authorization.Id);
        var totalCount = await ordered.LongCountAsync(cancellationToken);
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        return new PageResult<AgentKeyRecoveryAuthorization>(
            items,
            page,
            pageSize,
            totalCount);
    }

    public async Task<ManagedAgentDetail?> GetDetailAsync(
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var agent = await BuildAgentListQuery(agentId: agentId)
            .SingleOrDefaultAsync(cancellationToken);
        return agent is null
            ? null
            : new ManagedAgentDetail(
                agent,
                await GetLatestSnapshotAsync(agentId, cancellationToken));
    }

    public async Task<ManagedAgentDetail?> GetByAssetIdAsync(
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var agent = await BuildAgentListQuery(assetId: assetId)
            .SingleOrDefaultAsync(cancellationToken);
        return agent is null
            ? null
            : new ManagedAgentDetail(
                agent,
                await GetLatestSnapshotAsync(agent.Id, cancellationToken));
    }

    public void Add(AgentEnrollmentProfile profile) =>
        _dbContext.AgentEnrollmentProfiles.Add(profile);

    public void Add(ManagedAgent agent) =>
        _dbContext.ManagedAgents.Add(agent);

    public void Add(AgentKeyRecoveryAuthorization authorization) =>
        _dbContext.AgentKeyRecoveryAuthorizations.Add(authorization);

    public void Add(ManagedAgentSnapshot snapshot) =>
        _dbContext.ManagedAgentSnapshots.Add(snapshot);

    public void AddAuditLog(AuditLog auditLog) =>
        _dbContext.AuditLogs.Add(auditLog);

    public async Task ApplySnapshotRetentionAsync(
        Guid agentId,
        int maximumRecords,
        long maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.IsNpgsql())
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 WITH "OrderedSnapshots" AS
                 (
                     SELECT
                         "Id",
                         "Sequence",
                         ROW_NUMBER() OVER (
                             ORDER BY "Sequence" DESC
                         ) AS "RecordNumber",
                         SUM(
                             CASE
                                 WHEN "PayloadJson" IS NULL THEN 0
                                 ELSE OCTET_LENGTH(
                                     CONVERT_TO("PayloadJson", 'UTF8'))
                             END
                         ) OVER (
                             ORDER BY "Sequence" DESC
                             ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                         ) AS "CumulativePayloadBytes",
                         MAX(
                             CASE
                                 WHEN "PayloadJson" IS NOT NULL THEN "Sequence"
                                 ELSE NULL
                             END
                         ) OVER () AS "LatestDetailedSequence"
                     FROM "ManagedAgentSnapshots"
                     WHERE "AgentId" = {agentId}
                 )
                 DELETE FROM "ManagedAgentSnapshots"
                 WHERE "Id" IN
                 (
                     SELECT "Id"
                     FROM "OrderedSnapshots"
                     WHERE "Sequence" <> "LatestDetailedSequence"
                       AND (
                           "RecordNumber" > {maximumRecords}
                           OR "CumulativePayloadBytes" > {maximumPayloadBytes}
                       )
                 )
                 """,
                cancellationToken);
            return;
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             WITH "OrderedSnapshots" AS
             (
                 SELECT
                     "Id",
                     "Sequence",
                     ROW_NUMBER() OVER (
                         ORDER BY "Sequence" DESC
                     ) AS "RecordNumber",
                     SUM(
                         CASE
                             WHEN "PayloadJson" IS NULL THEN 0
                             ELSE LENGTH(CAST("PayloadJson" AS BLOB))
                         END
                     ) OVER (
                         ORDER BY "Sequence" DESC
                         ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                     ) AS "CumulativePayloadBytes",
                     MAX(
                         CASE
                             WHEN "PayloadJson" IS NOT NULL THEN "Sequence"
                             ELSE NULL
                         END
                     ) OVER () AS "LatestDetailedSequence"
                 FROM "ManagedAgentSnapshots"
                 WHERE "AgentId" = {agentId}
             )
             DELETE FROM "ManagedAgentSnapshots"
             WHERE "Id" IN
             (
                 SELECT "Id"
                 FROM "OrderedSnapshots"
                 WHERE "Sequence" <> "LatestDetailedSequence"
                   AND (
                       "RecordNumber" > {maximumRecords}
                       OR "CumulativePayloadBytes" > {maximumPayloadBytes}
                   )
             )
             """,
            cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesWithConflictMappingAsync(
                "agent_persistence_conflict",
                "The endpoint-agent record conflicts with existing data.",
                "The endpoint-agent record changed during this operation.",
                cancellationToken);
        }
        catch (PersistenceConflictException exception)
            when (exception.GetBaseException().Message.Contains(
                "agent_asset_terminal",
                StringComparison.Ordinal))
        {
            throw new PersistenceConflictException(
                "agent_asset_terminal",
                "Retired or disposed assets cannot be linked to endpoint agents.",
                exception);
        }
    }

    private IQueryable<ManagedAgentListItem> BuildAgentListQuery(
        bool includeRevoked = true,
        bool? isRevoked = null,
        ManagedAgentMatchState? matchState = null,
        ManagedAgentConnectivityStatus? connectivity = null,
        string? normalizedQuery = null,
        DateTimeOffset? onlineCutoffUtc = null,
        DateTimeOffset? staleCutoffUtc = null,
        Guid? agentId = null,
        Guid? assetId = null,
        bool orderForList = false)
    {
        var agents = _dbContext.ManagedAgents.AsNoTracking();
        if (isRevoked.HasValue)
        {
            agents = isRevoked.Value
                ? agents.Where(agent => agent.RevokedAtUtc != null)
                : agents.Where(agent => agent.RevokedAtUtc == null);
        }
        else if (!includeRevoked)
        {
            agents = agents.Where(agent => agent.RevokedAtUtc == null);
        }

        if (matchState.HasValue)
        {
            agents = agents.Where(
                agent => agent.MatchState == matchState.Value);
        }

        if (!string.IsNullOrEmpty(normalizedQuery))
        {
            agents = agents.Where(
                agent =>
                    agent.DeviceIdNormalized.Contains(normalizedQuery) ||
                    agent.HostnameNormalized.Contains(normalizedQuery) ||
                    (agent.SerialNumberNormalized != null &&
                     agent.SerialNumberNormalized.Contains(normalizedQuery)) ||
                    (agent.BiosUuidNormalized != null &&
                     agent.BiosUuidNormalized.Contains(normalizedQuery)) ||
                    _dbContext.Assets.Any(
                        linkedAsset =>
                            linkedAsset.Id == agent.LinkedAssetId &&
                            linkedAsset.AssetTagNormalized.Contains(
                                normalizedQuery)));
        }

        if (connectivity.HasValue)
        {
            if (!onlineCutoffUtc.HasValue || !staleCutoffUtc.HasValue)
            {
                throw new InvalidOperationException(
                    "Connectivity filters require both UTC cutoffs.");
            }

            agents = connectivity.Value switch
            {
                ManagedAgentConnectivityStatus.Online =>
                    agents.Where(
                        agent =>
                            agent.LastCheckInAtUtc != null &&
                            agent.LastCheckInAtUtc >= onlineCutoffUtc.Value),
                ManagedAgentConnectivityStatus.Stale =>
                    agents.Where(
                        agent =>
                            agent.LastCheckInAtUtc != null &&
                            agent.LastCheckInAtUtc < onlineCutoffUtc.Value &&
                            agent.LastCheckInAtUtc >= staleCutoffUtc.Value),
                ManagedAgentConnectivityStatus.Offline =>
                    agents.Where(
                        agent =>
                            agent.LastCheckInAtUtc != null &&
                            agent.LastCheckInAtUtc < staleCutoffUtc.Value),
                ManagedAgentConnectivityStatus.NeverCheckedIn =>
                    agents.Where(agent => agent.LastCheckInAtUtc == null),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(connectivity),
                    connectivity,
                    null),
            };
        }

        if (agentId.HasValue)
        {
            agents = agents.Where(agent => agent.Id == agentId.Value);
        }

        if (assetId.HasValue)
        {
            agents = agents.Where(
                agent => agent.LinkedAssetId == assetId.Value);
        }

        if (orderForList)
        {
            agents = agents
                .OrderByDescending(agent => agent.LastCheckInAtUtc)
                .ThenBy(agent => agent.Hostname);
        }

        return
            from agent in agents
            join profile in _dbContext.AgentEnrollmentProfiles.AsNoTracking()
                on agent.EnrollmentProfileId equals profile.Id
            join asset in _dbContext.Assets.AsNoTracking()
                on agent.LinkedAssetId equals (Guid?)asset.Id into assets
            from asset in assets.DefaultIfEmpty()
            select new ManagedAgentListItem(
                agent.Id,
                agent.EnrollmentProfileId,
                profile.Name,
                agent.DeviceId,
                agent.Hostname,
                agent.SerialNumber,
                agent.BiosUuid,
                agent.Manufacturer,
                agent.Model,
                agent.KeyProtection,
                agent.RevokedAtUtc != null,
                agent.MatchState,
                agent.MatchReason,
                agent.LinkedAssetId,
                asset == null ? null : asset.AssetTag,
                asset == null ? null : asset.DeviceName,
                agent.EnrolledAtUtc,
                agent.LastCheckInAtUtc,
                agent.LastCollectedAtUtc,
                agent.LastSeenIpAddress,
                agent.LastAgentVersion,
                agent.LastSequence,
                agent.RevokedAtUtc,
                agent.RevokedReason,
                agent.KeyReplacedAtUtc,
                agent.KeyReplacementCount,
                agent.Version);
    }

    private async Task<AgentDeviceSnapshot?> GetLatestSnapshotAsync(
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var latestReceipt = await _dbContext.ManagedAgentSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.AgentId == agentId)
            .OrderByDescending(snapshot => snapshot.Sequence)
            .Select(
                snapshot => new
                {
                    snapshot.SnapshotId,
                    snapshot.CollectedAtUtc,
                })
            .FirstOrDefaultAsync(cancellationToken);
        var payload = await _dbContext.ManagedAgentSnapshots
            .AsNoTracking()
            .Where(
                snapshot =>
                    snapshot.AgentId == agentId &&
                    snapshot.PayloadJson != null)
            .OrderByDescending(snapshot => snapshot.Sequence)
            .Select(snapshot => snapshot.PayloadJson!)
            .FirstOrDefaultAsync(cancellationToken);
        if (payload is null)
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<AgentDeviceSnapshot>(
            payload,
            SnapshotJsonOptions);
        return snapshot is null || latestReceipt is null
            ? snapshot
            : snapshot with
            {
                SnapshotId = latestReceipt.SnapshotId,
                CollectedAtUtc = latestReceipt.CollectedAtUtc,
            };
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

    private sealed class EfAgentTransaction : IAgentTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfAgentTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync(CancellationToken cancellationToken) =>
            _transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken) =>
            _transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }
}
