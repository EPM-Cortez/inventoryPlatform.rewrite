using Inventory.Application.DataExchange;
using Inventory.Domain.Auditing;
using Inventory.Domain.Importing;

namespace Inventory.Application.Abstractions;

public interface IReconciliationTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface ILegacyReconciliationRepository
{
    Task<IReconciliationTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken);

    Task<long> CountAssetsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LegacyTargetAssetSnapshot>> GetTargetAssetsAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken);

    Task<LegacyReconciliationRun?> GetRunByImportJobAsync(
        Guid importJobId,
        CancellationToken cancellationToken);

    Task<LegacyReconciliationSignOff?> GetSignOffAsync(
        Guid reconciliationRunId,
        CancellationToken cancellationToken);

    void AddRun(LegacyReconciliationRun run);

    void AddSignOff(LegacyReconciliationSignOff signOff);

    void AddAuditLog(AuditLog auditLog);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
