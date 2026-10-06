using Inventory.Application.Common;
using Inventory.Domain.Auditing;
using Inventory.Domain.Stocktakes;

namespace Inventory.Application.Stocktakes;

public interface IStocktakeRepository
{
    Task<IStocktakeTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken);

    Task<StocktakeSession?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<int> CountInProgressSessionsAsync(
        CancellationToken cancellationToken);

    Task<bool> HasMatchingInProgressSessionAsync(
        string normalizedName,
        StocktakeScope scope,
        Guid? scopeLocationId,
        Guid? scopeCategoryId,
        CancellationToken cancellationToken);

    Task<PageResult<StocktakeSessionListItem>> SearchSessionsAsync(
        StocktakeSessionSearch search,
        CancellationToken cancellationToken);

    Task<StocktakeReferenceView?> GetLocationAsync(
        Guid locationId,
        CancellationToken cancellationToken);

    Task<StocktakeReferenceView?> GetCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StocktakeAssetSnapshotSource>> ListSnapshotSourcesAsync(
        StocktakeScope scope,
        Guid? scopeLocationId,
        Guid? scopeCategoryId,
        int maximumResults,
        CancellationToken cancellationToken);

    Task<StocktakeAssetSnapshotSource?> FindAssetAsync(
        Guid? assetId,
        string? normalizedAssetTag,
        CancellationToken cancellationToken);

    Task<StocktakeObservation?> GetObservationAsync(
        Guid sessionId,
        Guid assetId,
        CancellationToken cancellationToken);

    Task<StocktakeProgress> GetProgressAsync(
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<PageResult<StocktakeItemView>> GetItemsAsync(
        Guid sessionId,
        StocktakeItemSearch search,
        CancellationToken cancellationToken);

    Task<StocktakeItemView?> GetItemAsync(
        Guid sessionId,
        Guid assetId,
        CancellationToken cancellationToken);

    void AddSession(StocktakeSession session);

    void AddExpectedAssets(IEnumerable<StocktakeExpectedAsset> expectedAssets);

    void AddObservation(StocktakeObservation observation);

    void AddAudit(AuditLog auditLog);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IStocktakeTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}
