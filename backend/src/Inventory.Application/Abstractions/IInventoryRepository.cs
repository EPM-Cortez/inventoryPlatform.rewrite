using Inventory.Application.Assets;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Inventory.Domain.Locations;

namespace Inventory.Application.Abstractions;

public interface IInventoryRepository
{
    Task<Asset?> GetAssetAsync(Guid id, CancellationToken cancellationToken);

    Task<AssetView?> GetAssetViewAsync(Guid id, CancellationToken cancellationToken);

    Task<AssetView?> GetAssetViewByTagAsync(string normalizedAssetTag, CancellationToken cancellationToken);

    Task<PageResult<AssetListItem>> SearchAssetsAsync(
        AssetSearch search,
        CancellationToken cancellationToken);

    Task<bool> AssetTagExistsAsync(
        string normalizedAssetTag,
        Guid? excludingAssetId,
        CancellationToken cancellationToken);

    Task<bool> SerialNumberExistsAsync(
        string normalizedSerialNumber,
        Guid? excludingAssetId,
        CancellationToken cancellationToken);

    Task<Guid?> GetLinkedManagedAgentIdAsync(
        Guid assetId,
        CancellationToken cancellationToken);

    Task<AssetCategory?> GetCategoryAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> CategoryCodeExistsAsync(
        string normalizedCode,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AssetCategory>> ListCategoriesAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<Location?> GetLocationAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> LocationCodeExistsAsync(
        string normalizedCode,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Location>> ListLocationsAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<PageResult<AssetEvent>> GetAssetHistoryAsync(
        Guid assetId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    void AddAsset(Asset asset);

    void AddAssetEvent(AssetEvent assetEvent);

    void AddCategory(AssetCategory category);

    void AddLocation(Location location);

    void AddAuditLog(AuditLog auditLog);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
