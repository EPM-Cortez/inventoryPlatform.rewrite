using System.Data;
using Inventory.Application.Abstractions;
using Inventory.Application.Bundles;
using Inventory.Application.Common;
using Inventory.Domain.AssetTags;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Inventory.Domain.BulkChanges;
using Inventory.Domain.Bundles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Infrastructure.Data;

public sealed class EfAssetOperationsRepository : IAssetOperationsRepository
{
    private readonly InventoryDbContext _dbContext;

    public EfAssetOperationsRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IAssetOperationsTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        return new EfAssetOperationsTransaction(transaction);
    }

    public Task<AssetTagPolicy?> GetAssetTagPolicyAsync(
        CancellationToken cancellationToken) =>
        _dbContext.Set<AssetTagPolicy>().SingleOrDefaultAsync(
            policy => policy.Id == AssetTagPolicy.SingletonId,
            cancellationToken);

    public async Task<string?> ReserveNextAssetTagAsync(
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE "AssetTagPolicies"
                SET "NextNumber" = "NextNumber" + 1,
                    "Version" = @version
                WHERE "Id" = 1
                  AND length(CAST("NextNumber" AS TEXT)) <= "NumericWidth"
                RETURNING "Prefix", "NumericWidth", "NextNumber" - 1
                """;
            var versionParameter = command.CreateParameter();
            versionParameter.ParameterName = "@version";
            versionParameter.DbType = DbType.Guid;
            versionParameter.Value = Guid.NewGuid();
            command.Parameters.Add(versionParameter);
            if (_dbContext.Database.CurrentTransaction is { } currentTransaction)
            {
                command.Transaction = currentTransaction.GetDbTransaction();
            }

            string? generatedTag;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }

                var prefix = reader.GetString(0);
                var numericWidth = reader.GetInt32(1);
                var allocatedNumber = reader.GetInt64(2);
                generatedTag = AssetTagPolicy.Format(
                    prefix,
                    numericWidth,
                    allocatedNumber);
            }

            var trackedPolicy = _dbContext.ChangeTracker
                .Entries<AssetTagPolicy>()
                .SingleOrDefault(
                    entry => entry.Entity.Id == AssetTagPolicy.SingletonId);
            if (trackedPolicy is not null)
            {
                await trackedPolicy.ReloadAsync(cancellationToken);
            }

            return generatedTag;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    public Task<AssetBulkChangeJob?> GetBulkChangeJobForUpdateAsync(
        Guid jobId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<AssetBulkChangeJob>().SingleOrDefaultAsync(
            job => job.Id == jobId,
            cancellationToken);

    public Task<AssetBulkChangeJob?> GetReusableBulkChangeJobAsync(
        Guid createdByUserId,
        string fingerprint,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        _dbContext.Set<AssetBulkChangeJob>()
            .AsNoTracking()
            .Where(
                job =>
                    job.CreatedByUserId == createdByUserId &&
                    job.Fingerprint == fingerprint &&
                    job.Status == AssetBulkChangeJobStatus.Previewed &&
                    job.ExpiresAtUtc > now)
            .OrderByDescending(job => job.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountActiveBulkChangeJobsAsync(
        Guid createdByUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        _dbContext.Set<AssetBulkChangeJob>()
            .CountAsync(
                job =>
                    job.CreatedByUserId == createdByUserId &&
                    job.Status == AssetBulkChangeJobStatus.Previewed &&
                    job.ExpiresAtUtc > now,
                cancellationToken);

    public async Task<IReadOnlyList<Asset>> GetAssetsForUpdateAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken) =>
        await _dbContext.Assets
            .Where(asset => assetIds.Contains(asset.Id))
            .ToArrayAsync(cancellationToken);

    public Task<AssetBundle?> GetBundleForUpdateAsync(
        Guid bundleId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<AssetBundle>()
            .Include(bundle => bundle.Members)
            .SingleOrDefaultAsync(
                bundle => bundle.Id == bundleId,
                cancellationToken);

    public async Task<AssetBundleView?> GetBundleViewAsync(
        Guid bundleId,
        CancellationToken cancellationToken)
    {
        var bundle = await _dbContext.Set<AssetBundle>()
            .AsNoTracking()
            .Include(row => row.Members)
            .SingleOrDefaultAsync(row => row.Id == bundleId, cancellationToken);
        if (bundle is null)
        {
            return null;
        }

        var currentMembers = bundle.CurrentMembers
            .OrderBy(member => member.Role)
            .ThenBy(member => member.AssetId)
            .ToArray();
        var assetIds = currentMembers.Select(member => member.AssetId).ToArray();
        var tags = await _dbContext.Assets
            .AsNoTracking()
            .Where(asset => assetIds.Contains(asset.Id))
            .ToDictionaryAsync(
                asset => asset.Id,
                asset => asset.AssetTag,
                cancellationToken);
        return new AssetBundleView(
            bundle.Id,
            bundle.Code,
            bundle.Name,
            bundle.Description,
            currentMembers
                .Select(
                    member => new AssetBundleMemberView(
                        member.Id,
                        member.AssetId,
                        tags.GetValueOrDefault(member.AssetId, "(missing asset)"),
                        member.Role,
                        member.OtherRole,
                        member.AddedAtUtc))
                .ToArray(),
            bundle.CreatedAtUtc,
            bundle.UpdatedAtUtc,
            bundle.ArchivedAtUtc,
            bundle.Version);
    }

    public async Task<PageResult<AssetBundleListItem>> SearchBundlesAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Set<AssetBundle>().AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(bundle => bundle.ArchivedAtUtc == null);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(bundle => bundle.Name)
            .ThenBy(bundle => bundle.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(
                bundle => new AssetBundleListItem(
                    bundle.Id,
                    bundle.Code,
                    bundle.Name,
                    bundle.Description,
                    bundle.Members.Count(member => member.RemovedAtUtc == null),
                    bundle.ArchivedAtUtc != null,
                    bundle.UpdatedAtUtc,
                    bundle.Version))
            .ToArrayAsync(cancellationToken);
        return new PageResult<AssetBundleListItem>(
            items,
            page,
            pageSize,
            totalCount);
    }

    public Task<bool> BundleCodeExistsAsync(
        string normalizedCode,
        Guid? excludingBundleId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<AssetBundle>().AnyAsync(
            bundle =>
                bundle.CodeNormalized == normalizedCode &&
                (!excludingBundleId.HasValue ||
                 bundle.Id != excludingBundleId.Value),
            cancellationToken);

    public Task<bool> BundleNameExistsAsync(
        string normalizedName,
        Guid? excludingBundleId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<AssetBundle>().AnyAsync(
            bundle =>
                bundle.NameNormalized == normalizedName &&
                (!excludingBundleId.HasValue ||
                 bundle.Id != excludingBundleId.Value),
            cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetAssetsInOtherActiveBundlesAsync(
        IReadOnlyCollection<Guid> assetIds,
        Guid? excludingBundleId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Set<AssetBundleMember>()
            .AsNoTracking()
            .Where(
                member =>
                    member.IsActive &&
                    assetIds.Contains(member.AssetId));
        if (excludingBundleId.HasValue)
        {
            query = query.Where(
                member => member.BundleId != excludingBundleId.Value);
        }

        return (await query
                .Select(member => member.AssetId)
                .Distinct()
                .ToArrayAsync(cancellationToken))
            .ToHashSet();
    }

    public void Add(AssetBulkChangeJob job) =>
        _dbContext.Set<AssetBulkChangeJob>().Add(job);

    public void Add(AssetBundle bundle) =>
        _dbContext.Set<AssetBundle>().Add(bundle);

    public void AddAssetEvent(AssetEvent assetEvent) =>
        _dbContext.AssetEvents.Add(assetEvent);

    public void AddAuditLog(AuditLog auditLog) =>
        _dbContext.AuditLogs.Add(auditLog);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesWithConflictMappingAsync(
                "data_conflict",
                "The requested asset operation conflicts with existing data.",
                "The record was changed by another user.",
                cancellationToken);
        }
        catch (PersistenceConflictException exception)
            when (exception.GetBaseException().Message.Contains(
                "IX_AssetBundleMembers_AssetId_Active",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PersistenceConflictException(
                "bundle_asset_already_active",
                "An asset already belongs to another active bundle.",
                exception);
        }
        catch (PersistenceConflictException exception)
            when (exception.GetBaseException().Message.Contains(
                "bundle_asset_terminal",
                StringComparison.Ordinal))
        {
            throw new PersistenceConflictException(
                "bundle_asset_terminal",
                "Retired or disposed assets cannot belong to an active bundle.",
                exception);
        }
        catch (PersistenceConflictException exception)
            when (exception.GetBaseException().Message.Contains(
                "bulk_preview_limit_reached",
                StringComparison.Ordinal))
        {
            throw new PersistenceConflictException(
                "bulk_preview_limit_reached",
                "The user already has the maximum number of active bulk previews.",
                exception);
        }
    }

    private sealed class EfAssetOperationsTransaction
        : IAssetOperationsTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfAssetOperationsTransaction(IDbContextTransaction transaction)
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
