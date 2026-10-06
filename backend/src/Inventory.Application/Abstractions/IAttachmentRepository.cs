using Inventory.Application.Attachments;
using Inventory.Application.Common;
using Inventory.Domain.Assets;

namespace Inventory.Application.Abstractions;

public interface IAttachmentRepository
{
    Task<AssetAttachment?> GetAsync(
        Guid assetId,
        Guid attachmentId,
        CancellationToken cancellationToken);

    Task<AssetAttachmentView?> GetViewAsync(
        Guid assetId,
        Guid attachmentId,
        CancellationToken cancellationToken);

    Task<PageResult<AssetAttachmentView>> ListAsync(
        Guid assetId,
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<AssetAttachmentStorageUsage> GetStorageUsageAsync(
        Guid assetId,
        CancellationToken cancellationToken);

    void Add(AssetAttachment attachment);
}
