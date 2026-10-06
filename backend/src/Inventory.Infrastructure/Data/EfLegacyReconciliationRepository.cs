using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Application.DataExchange;
using Inventory.Domain.Auditing;
using Inventory.Domain.Importing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Infrastructure.Data;

public sealed class EfLegacyReconciliationRepository
    : ILegacyReconciliationRepository
{
    private readonly InventoryDbContext _dbContext;

    public EfLegacyReconciliationRepository(
        InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReconciliationTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);
        return new EfReconciliationTransaction(transaction);
    }

    public Task<long> CountAssetsAsync(
        CancellationToken cancellationToken) =>
        _dbContext.Assets
            .AsNoTracking()
            .LongCountAsync(cancellationToken);

    public async Task<IReadOnlyList<LegacyTargetAssetSnapshot>>
        GetTargetAssetsAsync(
            IReadOnlyCollection<Guid> assetIds,
            CancellationToken cancellationToken)
    {
        if (assetIds.Count == 0)
        {
            return [];
        }

        return await (
            from asset in _dbContext.Assets.AsNoTracking()
            join category in _dbContext.AssetCategories.AsNoTracking()
                on asset.CategoryId equals category.Id
            join location in _dbContext.Locations.AsNoTracking()
                on asset.LocationId equals location.Id into locations
            from location in locations.DefaultIfEmpty()
            where assetIds.Contains(asset.Id)
            select new LegacyTargetAssetSnapshot(
                asset.Id,
                asset.AssetTag,
                asset.DeviceName,
                asset.SerialNumber,
                category.Code,
                asset.Status,
                location == null ? null : location.Code,
                asset.LegacyId))
            .ToArrayAsync(cancellationToken);
    }

    public Task<LegacyReconciliationRun?> GetRunByImportJobAsync(
        Guid importJobId,
        CancellationToken cancellationToken) =>
        _dbContext.LegacyReconciliationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                run => run.AssetImportJobId == importJobId,
                cancellationToken);

    public Task<LegacyReconciliationSignOff?> GetSignOffAsync(
        Guid reconciliationRunId,
        CancellationToken cancellationToken) =>
        _dbContext.LegacyReconciliationSignOffs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                signOff =>
                    signOff.ReconciliationRunId ==
                    reconciliationRunId,
                cancellationToken);

    public void AddRun(LegacyReconciliationRun run) =>
        _dbContext.LegacyReconciliationRuns.Add(run);

    public void AddSignOff(
        LegacyReconciliationSignOff signOff) =>
        _dbContext.LegacyReconciliationSignOffs.Add(signOff);

    public void AddAuditLog(AuditLog auditLog) =>
        _dbContext.AuditLogs.Add(auditLog);

    public Task SaveChangesAsync(
        CancellationToken cancellationToken) =>
        _dbContext.SaveChangesWithConflictMappingAsync(
            "reconciliation_conflict",
            "The reconciliation evidence conflicts with an existing run or sign-off.",
            "The reconciliation evidence changed.",
            cancellationToken);

    private sealed class EfReconciliationTransaction
        : IReconciliationTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfReconciliationTransaction(
            IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync(
            CancellationToken cancellationToken) =>
            _transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() =>
            _transaction.DisposeAsync();
    }
}
