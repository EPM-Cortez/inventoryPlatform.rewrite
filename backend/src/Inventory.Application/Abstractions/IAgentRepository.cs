using Inventory.Application.Agents;
using Inventory.Application.Common;
using Inventory.Domain.Agents;
using Inventory.Domain.Auditing;

namespace Inventory.Application.Abstractions;

public interface IAgentRepository
{
    Task<IAgentTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken);

    Task<AgentEnrollmentProfile?> GetEnrollmentProfileForUpdateAsync(
        Guid profileId,
        CancellationToken cancellationToken);

    Task<ManagedAgent?> GetAgentByDeviceIdForUpdateAsync(
        string normalizedDeviceId,
        CancellationToken cancellationToken);

    Task<ManagedAgent?> GetAgentForUpdateAsync(
        Guid agentId,
        CancellationToken cancellationToken);

    Task<bool> AgentExistsAsync(
        Guid agentId,
        CancellationToken cancellationToken);

    Task<AgentKeyRecoveryAuthorization?>
        GetKeyRecoveryAuthorizationForUpdateAsync(
            Guid authorizationId,
            CancellationToken cancellationToken);

    Task<AgentKeyRecoveryAuthorization?>
        GetPendingKeyRecoveryAuthorizationForUpdateAsync(
            Guid agentId,
            CancellationToken cancellationToken);

    Task<ManagedAgentSnapshot?> GetSnapshotAsync(
        Guid agentId,
        Guid snapshotId,
        CancellationToken cancellationToken);

    Task<ManagedAgentSnapshot?> GetLatestDetailedSnapshotAsync(
        Guid agentId,
        CancellationToken cancellationToken);

    Task DeleteExpiredRequestNoncesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> TryReserveRequestNonceAsync(
        AgentRequestNonce nonce,
        CancellationToken cancellationToken);

    Task<AgentAssetMatchResult> FindUnlinkedAssetMatchAsync(
        string? normalizedSerialNumber,
        Guid? biosGuid,
        CancellationToken cancellationToken);

    Task<AgentAssetLinkTarget?> GetAssetLinkTargetAsync(
        Guid assetId,
        CancellationToken cancellationToken);

    Task<bool> AssetLinkedToOtherAgentAsync(
        Guid assetId,
        Guid agentId,
        CancellationToken cancellationToken);

    Task<PageResult<ManagedAgentListItem>> SearchAsync(
        ManagedAgentSearch search,
        CancellationToken cancellationToken);

    Task<ManagedAgentFleetSummary> GetFleetSummaryAsync(
        bool includeRevoked,
        DateTimeOffset onlineCutoffUtc,
        DateTimeOffset staleCutoffUtc,
        CancellationToken cancellationToken);

    Task<PageResult<AgentEnrollmentProfileView>> SearchEnrollmentProfilesAsync(
        bool includeRevoked,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<PageResult<AgentKeyRecoveryAuthorization>>
        SearchKeyRecoveryAuthorizationsAsync(
            Guid agentId,
            bool includeClosed,
            DateTimeOffset now,
            int page,
            int pageSize,
            CancellationToken cancellationToken);

    Task<ManagedAgentDetail?> GetDetailAsync(
        Guid agentId,
        CancellationToken cancellationToken);

    Task<ManagedAgentDetail?> GetByAssetIdAsync(
        Guid assetId,
        CancellationToken cancellationToken);

    void Add(AgentEnrollmentProfile profile);

    void Add(ManagedAgent agent);

    void Add(AgentKeyRecoveryAuthorization authorization);

    void Add(ManagedAgentSnapshot snapshot);

    void AddAuditLog(AuditLog auditLog);

    Task ApplySnapshotRetentionAsync(
        Guid agentId,
        int maximumRecords,
        long maximumPayloadBytes,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAgentTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}
