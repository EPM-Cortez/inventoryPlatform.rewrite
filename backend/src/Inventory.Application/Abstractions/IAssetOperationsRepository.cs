using Inventory.Application.Bundles;
using Inventory.Application.Common;
using Inventory.Domain.AssetTags;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Inventory.Domain.BulkChanges;
using Inventory.Domain.Bundles;

namespace Inventory.Application.Abstractions;

public interface IAssetOperationsRepository
{
    Task<IAssetOperationsTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken);

    Task<AssetTagPolicy?> GetAssetTagPolicyAsync(
        CancellationToken cancellationToken);

    Task<string?> ReserveNextAssetTagAsync(
        CancellationToken cancellationToken);

    Task<AssetBulkChangeJob?> GetBulkChangeJobForUpdateAsync(
        Guid jobId,
        CancellationToken cancellationToken);

    Task<AssetBulkChangeJob?> GetReusableBulkChangeJobAsync(
        Guid createdByUserId,
        string fingerprint,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> CountActiveBulkChangeJobsAsync(
        Guid createdByUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Asset>> GetAssetsForUpdateAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken);

    Task<AssetBundle?> GetBundleForUpdateAsync(
        Guid bundleId,
        CancellationToken cancellationToken);

    Task<AssetBundleView?> GetBundleViewAsync(
        Guid bundleId,
        CancellationToken cancellationToken);

    Task<PageResult<AssetBundleListItem>> SearchBundlesAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<bool> BundleCodeExistsAsync(
        string normalizedCode,
        Guid? excludingBundleId,
        CancellationToken cancellationToken);

    Task<bool> BundleNameExistsAsync(
        string normalizedName,
        Guid? excludingBundleId,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> GetAssetsInOtherActiveBundlesAsync(
        IReadOnlyCollection<Guid> assetIds,
        Guid? excludingBundleId,
        CancellationToken cancellationToken);

    void Add(AssetBulkChangeJob job);

    void Add(AssetBundle bundle);

    void AddAssetEvent(AssetEvent assetEvent);

    void AddAuditLog(AuditLog auditLog);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAssetOperationsTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}
