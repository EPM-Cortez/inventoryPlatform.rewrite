using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Warranties;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class EfWarrantyRepository : IWarrantyRepository
{
    private readonly InventoryDbContext _dbContext;

    public EfWarrantyRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<WarrantyCoverage?> GetAsync(
        Guid warrantyId,
        CancellationToken cancellationToken) =>
        _dbContext.WarrantyCoverages.SingleOrDefaultAsync(
            warranty => warranty.Id == warrantyId,
            cancellationToken);

    public Task<WarrantyCoverage?> GetPrimaryAsync(
        Guid assetId,
        Guid? excludingWarrantyId,
        CancellationToken cancellationToken) =>
        _dbContext.WarrantyCoverages.SingleOrDefaultAsync(
            warranty =>
                warranty.AssetId == assetId &&
                warranty.IsPrimary &&
                warranty.ArchivedAtUtc == null &&
                (!excludingWarrantyId.HasValue ||
                 warranty.Id != excludingWarrantyId.Value),
            cancellationToken);

    public async Task<IReadOnlyList<WarrantyCoverage>> ListForAssetAsync(
        Guid assetId,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.WarrantyCoverages
            .AsNoTracking()
            .Where(warranty => warranty.AssetId == assetId);
        if (!includeArchived)
        {
            query = query.Where(warranty => warranty.ArchivedAtUtc == null);
        }

        return await query
            .OrderByDescending(warranty => warranty.IsPrimary)
            .ThenBy(warranty => warranty.EndDate)
            .ThenBy(warranty => warranty.Provider)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<PageResult<WarrantyCoverage>> ListExpiringAsync(
        DateOnly startsOnOrBefore,
        DateOnly endsOnOrBefore,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.WarrantyCoverages
            .AsNoTracking()
            .Where(warranty =>
                warranty.ArchivedAtUtc == null &&
                warranty.EndDate.HasValue &&
                warranty.EndDate.Value >= startsOnOrBefore &&
                warranty.EndDate.Value <= endsOnOrBefore);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(warranty => warranty.EndDate)
            .ThenBy(warranty => warranty.Provider)
            .ThenBy(warranty => warranty.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return new PageResult<WarrantyCoverage>(
            items,
            page,
            pageSize,
            totalCount);
    }

    public void Add(WarrantyCoverage warranty) =>
        _dbContext.WarrantyCoverages.Add(warranty);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesWithConflictMappingAsync(
            "warranty_conflict",
            "The warranty change conflicts with existing data.",
            "The warranty was changed by another user.",
            cancellationToken);
}
