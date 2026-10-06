using Inventory.Application.Common;
using Inventory.Application.Dashboard;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Locations;
using Inventory.Domain.People;
using Inventory.Domain.Warranties;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class EfDashboardRepository : IDashboardRepository
{
    private readonly InventoryDbContext _dbContext;

    public EfDashboardRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DashboardSummaryView> GetSummaryAsync(
        int categoryLimit,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var assets = _dbContext.Set<Asset>().AsNoTracking();
            var totalAssets = await assets.LongCountAsync(cancellationToken);
            var activeAssets = await assets.LongCountAsync(
                asset => asset.Status != AssetStatus.Retired &&
                         asset.Status != AssetStatus.Disposed,
                cancellationToken);
            var statusCounts = await assets
                .GroupBy(asset => asset.Status)
                .OrderBy(group => group.Key)
                .Select(group => new DashboardCount<AssetStatus>(group.Key, group.LongCount()))
                .ToArrayAsync(cancellationToken);
            var conditionCounts = await assets
                .GroupBy(asset => asset.Condition)
                .OrderBy(group => group.Key)
                .Select(group => new DashboardCount<AssetCondition>(group.Key, group.LongCount()))
                .ToArrayAsync(cancellationToken);

            var categoryRows = await (
                    from asset in assets.Where(
                        asset => asset.Status != AssetStatus.Retired &&
                                 asset.Status != AssetStatus.Disposed)
                    join category in _dbContext.Set<AssetCategory>().AsNoTracking()
                        on asset.CategoryId equals category.Id
                    group asset by new { category.Id, category.Code, category.Name }
                    into categoryGroup
                    orderby categoryGroup.LongCount() descending,
                        categoryGroup.Key.Name,
                        categoryGroup.Key.Id
                    select new DashboardCategoryCount(
                        categoryGroup.Key.Id,
                        categoryGroup.Key.Code,
                        categoryGroup.Key.Name,
                        categoryGroup.LongCount()))
                .Take(categoryLimit + 1)
                .ToArrayAsync(cancellationToken);

            var exceptionCounts = new[]
            {
            new DashboardExceptionCount(
                DashboardExceptionCodes.MissingLocation,
                "Assets without a recorded location",
                await ApplyExceptionFilter(assets, DashboardExceptionCodes.MissingLocation, today)
                    .LongCountAsync(cancellationToken)),
            new DashboardExceptionCount(
                DashboardExceptionCodes.MissingSerialNumber,
                "Assets without a serial number",
                await ApplyExceptionFilter(
                        assets,
                        DashboardExceptionCodes.MissingSerialNumber,
                        today)
                    .LongCountAsync(cancellationToken)),
            new DashboardExceptionCount(
                DashboardExceptionCodes.Repair,
                "Assets awaiting repair",
                await ApplyExceptionFilter(assets, DashboardExceptionCodes.Repair, today)
                    .LongCountAsync(cancellationToken)),
            new DashboardExceptionCount(
                DashboardExceptionCodes.LostOrStolen,
                "Assets marked lost or stolen",
                await ApplyExceptionFilter(assets, DashboardExceptionCodes.LostOrStolen, today)
                    .LongCountAsync(cancellationToken)),
            new DashboardExceptionCount(
                DashboardExceptionCodes.AssignedWithoutCustody,
                "Assigned assets without a current custody record",
                await ApplyExceptionFilter(
                        assets,
                        DashboardExceptionCodes.AssignedWithoutCustody,
                        today)
                    .LongCountAsync(cancellationToken)),
            new DashboardExceptionCount(
                DashboardExceptionCodes.InactivePersonCustody,
                "Assets held by inactive people",
                await ApplyExceptionFilter(
                        assets,
                        DashboardExceptionCodes.InactivePersonCustody,
                        today)
                    .LongCountAsync(cancellationToken)),
            new DashboardExceptionCount(
                DashboardExceptionCodes.MissingWarranty,
                "Active assets without warranty records",
                await ApplyExceptionFilter(
                        assets,
                        DashboardExceptionCodes.MissingWarranty,
                        today)
                    .LongCountAsync(cancellationToken)),
            new DashboardExceptionCount(
                DashboardExceptionCodes.ExpiredWarranty,
                "Active assets with an expired primary warranty",
                await ApplyExceptionFilter(
                        assets,
                        DashboardExceptionCodes.ExpiredWarranty,
                        today)
                    .LongCountAsync(cancellationToken)),
        };

            var summary = new DashboardSummaryView(
                totalAssets,
                activeAssets,
                statusCounts,
                conditionCounts,
                categoryRows.Take(categoryLimit).ToArray(),
                categoryRows.Length > categoryLimit,
                exceptionCounts);
            await transaction.CommitAsync(cancellationToken);
            return summary;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<InventoryDistributionView> GetInventoryDistributionAsync(
        InventoryDistributionSearch search,
        CancellationToken cancellationToken)
    {
        var assetQuery = _dbContext.Set<Asset>().AsNoTracking();
        if (!search.IncludeTerminalAssets)
        {
            assetQuery = assetQuery.Where(
                asset => asset.Status != AssetStatus.Retired &&
                         asset.Status != AssetStatus.Disposed);
        }

        var joined =
            from asset in assetQuery
            join category in _dbContext.Set<AssetCategory>().AsNoTracking()
                on asset.CategoryId equals category.Id
            join location in _dbContext.Set<Location>().AsNoTracking()
                on asset.LocationId equals (Guid?)location.Id into locations
            from location in locations.DefaultIfEmpty()
            select new
            {
                Asset = asset,
                CategoryId = category.Id,
                CategoryCode = category.Code,
                CategoryName = category.Name,
                LocationId = location == null ? (Guid?)null : location.Id,
                LocationCode = location == null ? null : location.Code,
                LocationName = location == null ? null : location.Name,
            };

        if (search.CategoryId.HasValue)
        {
            joined = joined.Where(item => item.CategoryId == search.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search.Query))
        {
            var term = search.Query;
            joined = joined.Where(
                item => item.CategoryCode.Contains(term) ||
                        item.CategoryName.Contains(term));
        }

        var matchingAssetCount = await joined.LongCountAsync(cancellationToken);
        var groups = joined
            .GroupBy(
                item => new
                {
                    item.CategoryId,
                    item.CategoryCode,
                    item.CategoryName,
                    item.LocationId,
                    item.LocationCode,
                    item.LocationName,
                });
        var totalGroups = await groups.LongCountAsync(cancellationToken);
        var items = await groups
            .OrderBy(group => group.Key.CategoryName)
            .ThenBy(group => group.Key.CategoryCode)
            .ThenBy(group => group.Key.LocationName ?? "\uffff")
            .ThenBy(group => group.Key.LocationCode)
            .ThenBy(group => group.Key.CategoryId)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(
                group => new InventoryDistributionItem(
                    group.Key.CategoryId,
                    group.Key.CategoryCode,
                    group.Key.CategoryName,
                    group.Key.LocationId,
                    group.Key.LocationCode,
                    group.Key.LocationName,
                    group.LongCount(),
                    group.LongCount(item => item.Asset.Status == AssetStatus.InStock),
                    group.LongCount(item => item.Asset.Status == AssetStatus.Assigned),
                    group.LongCount(
                        item => item.Asset.Status != AssetStatus.InStock &&
                                item.Asset.Status != AssetStatus.Assigned)))
            .ToArrayAsync(cancellationToken);

        return new InventoryDistributionView(
            new PageResult<InventoryDistributionItem>(
                items,
                search.Page,
                search.PageSize,
                totalGroups),
            matchingAssetCount);
    }

    public async Task<PageResult<DashboardExceptionAssetView>> GetExceptionQueueAsync(
        string exceptionCode,
        DateOnly today,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var assets = ApplyExceptionFilter(
            _dbContext.Set<Asset>().AsNoTracking(),
            exceptionCode,
            today);
        var totalCount = await assets.LongCountAsync(cancellationToken);
        var items = await (
                from asset in assets
                join category in _dbContext.Set<AssetCategory>().AsNoTracking()
                    on asset.CategoryId equals category.Id
                join location in _dbContext.Set<Location>().AsNoTracking()
                    on asset.LocationId equals (Guid?)location.Id into locations
                from location in locations.DefaultIfEmpty()
                orderby asset.UpdatedAtUtc descending, asset.AssetTag, asset.Id
                select new DashboardExceptionAssetView(
                    asset.Id,
                    asset.AssetTag,
                    asset.DeviceName,
                    category.Id,
                    category.Code,
                    category.Name,
                    asset.Status,
                    asset.Condition,
                    location == null ? null : location.Id,
                    location == null ? null : location.Code,
                    location == null ? null : location.Name,
                    asset.UpdatedAtUtc))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return new PageResult<DashboardExceptionAssetView>(
            items,
            page,
            pageSize,
            totalCount);
    }

    private IQueryable<Asset> ApplyExceptionFilter(
        IQueryable<Asset> assets,
        string exceptionCode,
        DateOnly today)
    {
        var assignments = _dbContext.Set<AssetAssignment>().AsNoTracking();
        var people = _dbContext.Set<Person>().AsNoTracking();
        var warranties = _dbContext.Set<WarrantyCoverage>().AsNoTracking();
        return exceptionCode switch
        {
            DashboardExceptionCodes.MissingLocation => assets.Where(
                asset => asset.Status != AssetStatus.Retired &&
                         asset.Status != AssetStatus.Disposed &&
                         asset.LocationId == null),
            DashboardExceptionCodes.MissingSerialNumber => assets.Where(
                asset => asset.Status != AssetStatus.Retired &&
                         asset.Status != AssetStatus.Disposed &&
                         asset.SerialNumber == null),
            DashboardExceptionCodes.Repair => assets.Where(
                asset => asset.Status == AssetStatus.Repair),
            DashboardExceptionCodes.LostOrStolen => assets.Where(
                asset => asset.Status == AssetStatus.Lost ||
                         asset.Status == AssetStatus.Stolen),
            DashboardExceptionCodes.AssignedWithoutCustody => assets.Where(
                asset => asset.Status == AssetStatus.Assigned &&
                         !assignments.Any(
                             assignment =>
                                 assignment.AssetId == asset.Id &&
                                 assignment.ReturnedAtUtc == null)),
            DashboardExceptionCodes.InactivePersonCustody => assets.Where(
                asset => assignments.Any(
                    assignment =>
                        assignment.AssetId == asset.Id &&
                        assignment.ReturnedAtUtc == null &&
                        assignment.PersonId.HasValue &&
                        people.Any(
                            person =>
                                person.Id == assignment.PersonId.Value &&
                                !person.IsActive))),
            DashboardExceptionCodes.MissingWarranty => assets.Where(
                asset => asset.Status != AssetStatus.Retired &&
                         asset.Status != AssetStatus.Disposed &&
                         !warranties.Any(
                             warranty =>
                                 warranty.AssetId == asset.Id &&
                                 warranty.ArchivedAtUtc == null)),
            DashboardExceptionCodes.ExpiredWarranty => assets.Where(
                asset => asset.Status != AssetStatus.Retired &&
                         asset.Status != AssetStatus.Disposed &&
                         warranties.Any(
                             warranty =>
                                 warranty.AssetId == asset.Id &&
                                 warranty.ArchivedAtUtc == null &&
                                 warranty.IsPrimary &&
                                 warranty.EndDate.HasValue &&
                                 warranty.EndDate.Value < today)),
            _ => assets.Where(_ => false),
        };
    }
}
