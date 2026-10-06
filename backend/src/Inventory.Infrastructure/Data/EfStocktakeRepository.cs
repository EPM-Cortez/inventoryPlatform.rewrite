using Inventory.Application.Common;
using Inventory.Application.Stocktakes;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Inventory.Domain.Locations;
using Inventory.Domain.Stocktakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Infrastructure.Data;

public sealed class EfStocktakeRepository : IStocktakeRepository
{
    private readonly InventoryDbContext _dbContext;

    public EfStocktakeRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IStocktakeTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        return new EfStocktakeTransaction(transaction);
    }

    public Task<StocktakeSession?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<StocktakeSession>().SingleOrDefaultAsync(
            session => session.Id == sessionId,
            cancellationToken);

    public Task<int> CountInProgressSessionsAsync(
        CancellationToken cancellationToken) =>
        _dbContext.Set<StocktakeSession>().CountAsync(
            session => session.Status == StocktakeStatus.InProgress,
            cancellationToken);

    // StringComparison overloads are not translated by both supported EF providers.
#pragma warning disable CA1304, CA1311, CA1862
    public Task<bool> HasMatchingInProgressSessionAsync(
        string normalizedName,
        StocktakeScope scope,
        Guid? scopeLocationId,
        Guid? scopeCategoryId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<StocktakeSession>().AnyAsync(
            session =>
                session.Status == StocktakeStatus.InProgress &&
                session.Scope == scope &&
                session.ScopeLocationId == scopeLocationId &&
                session.ScopeCategoryId == scopeCategoryId &&
                session.Name.ToUpper() == normalizedName,
            cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862

    public async Task<PageResult<StocktakeSessionListItem>> SearchSessionsAsync(
        StocktakeSessionSearch search,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Set<StocktakeSession>().AsNoTracking();
        if (search.Status.HasValue)
        {
            query = query.Where(session => session.Status == search.Status.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(session => session.StartedAtUtc)
            .ThenBy(session => session.Name)
            .ThenBy(session => session.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(
                session => new SessionListRow(
                    session.Id,
                    session.Name,
                    session.Scope,
                    session.Scope == StocktakeScope.All
                        ? "All assets"
                        : session.Scope == StocktakeScope.Location
                            ? _dbContext.Set<Location>()
                                .Where(location => location.Id == session.ScopeLocationId)
                                .Select(location => location.Name)
                                .FirstOrDefault() ?? "Unknown location"
                            : _dbContext.Set<AssetCategory>()
                                .Where(category => category.Id == session.ScopeCategoryId)
                                .Select(category => category.Name)
                                .FirstOrDefault() ?? "Unknown category",
                    session.Status,
                    session.StartedAtUtc,
                    session.StartedByDisplayName,
                    session.CompletedAtUtc ?? session.CancelledAtUtc,
                    session.Version))
            .ToArrayAsync(cancellationToken);
        var progress = await GetProgressMapAsync(
            rows.Select(row => row.Id).ToArray(),
            cancellationToken);

        return new PageResult<StocktakeSessionListItem>(
            rows.Select(
                    row => new StocktakeSessionListItem(
                        row.Id,
                        row.Name,
                        row.Scope,
                        row.ScopeDisplayName,
                        row.Status,
                        row.StartedAtUtc,
                        row.StartedByDisplayName,
                        row.ClosedAtUtc,
                        progress.GetValueOrDefault(row.Id, EmptyProgress),
                        row.Version))
                .ToArray(),
            search.Page,
            search.PageSize,
            totalCount);
    }

    public Task<StocktakeReferenceView?> GetLocationAsync(
        Guid locationId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<Location>()
            .AsNoTracking()
            .Where(location => location.Id == locationId)
            .Select(
                location => new StocktakeReferenceView(
                    location.Id,
                    location.Code,
                    location.Name,
                    location.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<StocktakeReferenceView?> GetCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<AssetCategory>()
            .AsNoTracking()
            .Where(category => category.Id == categoryId)
            .Select(
                category => new StocktakeReferenceView(
                    category.Id,
                    category.Code,
                    category.Name,
                    category.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<StocktakeAssetSnapshotSource>> ListSnapshotSourcesAsync(
        StocktakeScope scope,
        Guid? scopeLocationId,
        Guid? scopeCategoryId,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        var assets = _dbContext.Set<Asset>()
            .AsNoTracking()
            .Where(
                asset =>
                    asset.Status != AssetStatus.Retired &&
                    asset.Status != AssetStatus.Disposed);
        assets = scope switch
        {
            StocktakeScope.Location =>
                assets.Where(asset => asset.LocationId == scopeLocationId),
            StocktakeScope.Category =>
                assets.Where(asset => asset.CategoryId == scopeCategoryId),
            _ => assets,
        };

        return await ProjectAssetSources(
                assets
                    .OrderBy(asset => asset.AssetTag)
                    .ThenBy(asset => asset.Id)
                    .Take(maximumResults))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<StocktakeAssetSnapshotSource?> FindAssetAsync(
        Guid? assetId,
        string? normalizedAssetTag,
        CancellationToken cancellationToken)
    {
        var assets = _dbContext.Set<Asset>().AsNoTracking();
        if (assetId.HasValue)
        {
            assets = assets.Where(asset => asset.Id == assetId.Value);
        }
        else
        {
            assets = assets.Where(
                asset => asset.AssetTagNormalized == normalizedAssetTag);
        }

        return await ProjectAssetSources(assets)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<StocktakeObservation?> GetObservationAsync(
        Guid sessionId,
        Guid assetId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<StocktakeObservation>().SingleOrDefaultAsync(
            observation =>
                observation.SessionId == sessionId &&
                observation.AssetId == assetId,
            cancellationToken);

    public async Task<StocktakeProgress> GetProgressAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var map = await GetProgressMapAsync([sessionId], cancellationToken);
        return map.GetValueOrDefault(sessionId, EmptyProgress);
    }

    public async Task<PageResult<StocktakeItemView>> GetItemsAsync(
        Guid sessionId,
        StocktakeItemSearch search,
        CancellationToken cancellationToken)
    {
        var rows = BuildItemRowQuery(sessionId, search.Filter);
        var totalCount = await rows.LongCountAsync(cancellationToken);
        var items = await ProjectItemViews(
                rows
                    .OrderBy(item => item.AssetTag)
                    .ThenBy(item => item.AssetId)
                    .Skip((search.Page - 1) * search.PageSize)
                    .Take(search.PageSize))
            .ToArrayAsync(cancellationToken);
        return new PageResult<StocktakeItemView>(
            items,
            search.Page,
            search.PageSize,
            totalCount);
    }

    public Task<StocktakeItemView?> GetItemAsync(
        Guid sessionId,
        Guid assetId,
        CancellationToken cancellationToken) =>
        ProjectItemViews(
                BuildItemRowQuery(sessionId, StocktakeItemFilter.All)
                    .Where(item => item.AssetId == assetId))
            .SingleOrDefaultAsync(cancellationToken);

    public void AddSession(StocktakeSession session) =>
        _dbContext.Set<StocktakeSession>().Add(session);

    public void AddExpectedAssets(IEnumerable<StocktakeExpectedAsset> expectedAssets) =>
        _dbContext.Set<StocktakeExpectedAsset>().AddRange(expectedAssets);

    public void AddObservation(StocktakeObservation observation) =>
        _dbContext.Set<StocktakeObservation>().Add(observation);

    public void AddAudit(AuditLog auditLog) =>
        _dbContext.Set<AuditLog>().Add(auditLog);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesWithConflictMappingAsync(
                "stocktake_conflict",
                "The stocktake change conflicts with existing data.",
                "The stocktake was changed by another user.",
                cancellationToken);
        }
        catch (PersistenceConflictException exception)
            when (exception.GetBaseException().Message.Contains(
                "stocktake_active_limit_reached",
                StringComparison.Ordinal))
        {
            throw new PersistenceConflictException(
                "stocktake_active_limit_reached",
                "The maximum number of in-progress stocktakes has been reached.",
                exception);
        }
        catch (PersistenceConflictException exception)
            when (exception.GetBaseException().Message.Contains(
                "stocktake_duplicate_active",
                StringComparison.Ordinal))
        {
            throw new PersistenceConflictException(
                "stocktake_duplicate_active",
                "A matching in-progress stocktake already exists.",
                exception);
        }
    }

    private sealed class EfStocktakeTransaction : IStocktakeTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfStocktakeTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync(CancellationToken cancellationToken) =>
            _transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken) =>
            _transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }

    private IQueryable<StocktakeAssetSnapshotSource> ProjectAssetSources(
        IQueryable<Asset> assets) =>
        from asset in assets
        join category in _dbContext.Set<AssetCategory>().AsNoTracking()
            on asset.CategoryId equals category.Id
        join location in _dbContext.Set<Location>().AsNoTracking()
            on asset.LocationId equals (Guid?)location.Id into locations
        from location in locations.DefaultIfEmpty()
        select new StocktakeAssetSnapshotSource(
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
            location == null ? null : location.Name);

    private IQueryable<StocktakeItemRow> BuildItemRowQuery(
        Guid sessionId,
        StocktakeItemFilter filter)
    {
        var expectedItems =
            from expected in _dbContext.Set<StocktakeExpectedAsset>().AsNoTracking()
            where expected.SessionId == sessionId
            join observation in _dbContext.Set<StocktakeObservation>().AsNoTracking()
                on new { expected.SessionId, expected.AssetId }
                equals new { observation.SessionId, observation.AssetId } into observations
            from observation in observations.DefaultIfEmpty()
            select new StocktakeItemRow
            {
                AssetId = expected.AssetId,
                AssetTag = expected.AssetTag,
                DeviceName = expected.DeviceName,
                CategoryId = expected.CategoryId,
                CategoryCode = expected.CategoryCode,
                CategoryName = expected.CategoryName,
                IsExpected = true,
                IsSeen = observation != null,
                ExpectedStatus = expected.ExpectedStatus,
                ExpectedCondition = expected.ExpectedCondition,
                ExpectedLocationId = expected.ExpectedLocationId,
                ExpectedLocationCode = expected.ExpectedLocationCode,
                ExpectedLocationName = expected.ExpectedLocationName,
                ObservedLocationId =
                    observation == null ? null : observation.ObservedLocationId,
                ObservedLocationCode =
                    observation == null ? null : observation.ObservedLocationCode,
                ObservedLocationName =
                    observation == null ? null : observation.ObservedLocationName,
                ObservedCondition =
                    observation == null ? null : observation.ObservedCondition,
                Note = observation == null ? null : observation.Note,
                ObservedAtUtc =
                    observation == null ? null : observation.ObservedAtUtc,
                ObservedByUserId =
                    observation == null ? null : observation.ObservedByUserId,
                ObservedByDisplayName =
                    observation == null
                        ? null
                        : observation.ObservedByDisplayName,
                IsUnexpected = false,
                IsWrongLocation =
                    observation != null &&
                    expected.ExpectedLocationId != observation.ObservedLocationId,
                IsConditionChanged =
                    observation != null &&
                    expected.ExpectedCondition != observation.ObservedCondition,
            };

        var unexpectedItems =
            from observation in _dbContext.Set<StocktakeObservation>().AsNoTracking()
            where observation.SessionId == sessionId &&
                  !_dbContext.Set<StocktakeExpectedAsset>().Any(
                      expected =>
                          expected.SessionId == sessionId &&
                          expected.AssetId == observation.AssetId)
            select new StocktakeItemRow
            {
                AssetId = observation.AssetId,
                AssetTag = observation.AssetTag,
                DeviceName = observation.DeviceName,
                CategoryId = observation.CategoryId,
                CategoryCode = observation.CategoryCode,
                CategoryName = observation.CategoryName,
                IsExpected = false,
                IsSeen = true,
                ExpectedStatus = null,
                ExpectedCondition = null,
                ExpectedLocationId = null,
                ExpectedLocationCode = null,
                ExpectedLocationName = null,
                ObservedLocationId = observation.ObservedLocationId,
                ObservedLocationCode = observation.ObservedLocationCode,
                ObservedLocationName = observation.ObservedLocationName,
                ObservedCondition = observation.ObservedCondition,
                Note = observation.Note,
                ObservedAtUtc = observation.ObservedAtUtc,
                ObservedByUserId = observation.ObservedByUserId,
                ObservedByDisplayName = observation.ObservedByDisplayName,
                IsUnexpected = true,
                IsWrongLocation = false,
                IsConditionChanged = false,
            };

        return filter switch
        {
            StocktakeItemFilter.Seen =>
                expectedItems.Where(item => item.IsSeen),
            StocktakeItemFilter.Missing =>
                expectedItems.Where(item => !item.IsSeen),
            StocktakeItemFilter.Unexpected => unexpectedItems,
            StocktakeItemFilter.WrongLocation =>
                expectedItems.Where(item => item.IsWrongLocation),
            StocktakeItemFilter.ConditionChanged =>
                expectedItems.Where(item => item.IsConditionChanged),
            _ => expectedItems.Concat(unexpectedItems),
        };
    }

    private static IQueryable<StocktakeItemView> ProjectItemViews(
        IQueryable<StocktakeItemRow> rows) =>
        rows.Select(
            item => new StocktakeItemView(
                item.AssetId,
                item.AssetTag,
                item.DeviceName,
                item.CategoryId,
                item.CategoryCode,
                item.CategoryName,
                item.IsExpected,
                item.IsSeen,
                item.ExpectedStatus,
                item.ExpectedCondition,
                item.ExpectedLocationId,
                item.ExpectedLocationCode,
                item.ExpectedLocationName,
                item.ObservedLocationId,
                item.ObservedLocationCode,
                item.ObservedLocationName,
                item.ObservedCondition,
                item.Note,
                item.ObservedAtUtc,
                item.ObservedByUserId,
                item.ObservedByDisplayName,
                item.IsUnexpected,
                item.IsWrongLocation,
                item.IsConditionChanged));

    private async Task<Dictionary<Guid, StocktakeProgress>> GetProgressMapAsync(
        Guid[] sessionIds,
        CancellationToken cancellationToken)
    {
        if (sessionIds.Length == 0)
        {
            return [];
        }

        var expected = _dbContext.Set<StocktakeExpectedAsset>()
            .AsNoTracking()
            .Where(item => sessionIds.Contains(item.SessionId));
        var observations = _dbContext.Set<StocktakeObservation>()
            .AsNoTracking()
            .Where(item => sessionIds.Contains(item.SessionId));
        var expectedCounts = await expected
            .GroupBy(item => item.SessionId)
            .Select(
                group => new
                {
                    SessionId = group.Key,
                    Count = group.LongCount(),
                })
            .ToDictionaryAsync(row => row.SessionId, row => row.Count, cancellationToken);
        var seenPairs =
            from item in expected
            join observation in observations
                on new { item.SessionId, item.AssetId }
                equals new { observation.SessionId, observation.AssetId }
            select new
            {
                item.SessionId,
                item.ExpectedLocationId,
                item.ExpectedCondition,
                observation.ObservedLocationId,
                observation.ObservedCondition,
            };
        var seenCounts = await seenPairs
            .GroupBy(item => item.SessionId)
            .Select(
                group => new
                {
                    SessionId = group.Key,
                    Count = group.LongCount(),
                })
            .ToDictionaryAsync(row => row.SessionId, row => row.Count, cancellationToken);
        var wrongLocationCounts = await seenPairs
            .Where(item => item.ExpectedLocationId != item.ObservedLocationId)
            .GroupBy(item => item.SessionId)
            .Select(
                group => new
                {
                    SessionId = group.Key,
                    Count = group.LongCount(),
                })
            .ToDictionaryAsync(row => row.SessionId, row => row.Count, cancellationToken);
        var conditionChangedCounts = await seenPairs
            .Where(item => item.ExpectedCondition != item.ObservedCondition)
            .GroupBy(item => item.SessionId)
            .Select(
                group => new
                {
                    SessionId = group.Key,
                    Count = group.LongCount(),
                })
            .ToDictionaryAsync(row => row.SessionId, row => row.Count, cancellationToken);
        var unexpectedCounts = await observations
            .Where(
                observation => !_dbContext.Set<StocktakeExpectedAsset>().Any(
                    item =>
                        item.SessionId == observation.SessionId &&
                        item.AssetId == observation.AssetId))
            .GroupBy(item => item.SessionId)
            .Select(
                group => new
                {
                    SessionId = group.Key,
                    Count = group.LongCount(),
                })
            .ToDictionaryAsync(row => row.SessionId, row => row.Count, cancellationToken);

        return sessionIds.ToDictionary(
            sessionId => sessionId,
            sessionId =>
            {
                var expectedCount = expectedCounts.GetValueOrDefault(sessionId);
                var seenCount = seenCounts.GetValueOrDefault(sessionId);
                return new StocktakeProgress(
                    expectedCount,
                    seenCount,
                    expectedCount - seenCount,
                    unexpectedCounts.GetValueOrDefault(sessionId),
                    wrongLocationCounts.GetValueOrDefault(sessionId),
                    conditionChangedCounts.GetValueOrDefault(sessionId));
            });
    }

    private static StocktakeProgress EmptyProgress { get; } = new(0, 0, 0, 0, 0, 0);

    private sealed class StocktakeItemRow
    {
        public Guid AssetId { get; init; }

        public string AssetTag { get; init; } = string.Empty;

        public string? DeviceName { get; init; }

        public Guid CategoryId { get; init; }

        public string CategoryCode { get; init; } = string.Empty;

        public string CategoryName { get; init; } = string.Empty;

        public bool IsExpected { get; init; }

        public bool IsSeen { get; init; }

        public AssetStatus? ExpectedStatus { get; init; }

        public AssetCondition? ExpectedCondition { get; init; }

        public Guid? ExpectedLocationId { get; init; }

        public string? ExpectedLocationCode { get; init; }

        public string? ExpectedLocationName { get; init; }

        public Guid? ObservedLocationId { get; init; }

        public string? ObservedLocationCode { get; init; }

        public string? ObservedLocationName { get; init; }

        public AssetCondition? ObservedCondition { get; init; }

        public string? Note { get; init; }

        public DateTimeOffset? ObservedAtUtc { get; init; }

        public Guid? ObservedByUserId { get; init; }

        public string? ObservedByDisplayName { get; init; }

        public bool IsUnexpected { get; init; }

        public bool IsWrongLocation { get; init; }

        public bool IsConditionChanged { get; init; }
    }

    private sealed record SessionListRow(
        Guid Id,
        string Name,
        StocktakeScope Scope,
        string ScopeDisplayName,
        StocktakeStatus Status,
        DateTimeOffset StartedAtUtc,
        string StartedByDisplayName,
        DateTimeOffset? ClosedAtUtc,
        Guid Version);
}
