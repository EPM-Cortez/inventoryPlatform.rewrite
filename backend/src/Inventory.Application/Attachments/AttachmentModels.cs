namespace Inventory.Application.Attachments;

public enum AttachmentCategoryView
{
    Invoice,
    WarrantyDocument,
    DisposalCertificate,
    Photo,
    Other,
}

public sealed record UploadAssetAttachmentCommand(
    AttachmentCategoryView Category,
    string FileName,
    Stream Content);

public sealed record ArchiveAssetAttachmentCommand(Guid Version);

public sealed record AssetAttachmentView(
    Guid Id,
    Guid AssetId,
    AttachmentCategoryView Category,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    string CreatedByDisplayName,
    DateTimeOffset? ArchivedAtUtc,
    Guid? ArchivedByUserId,
    string? ArchivedByDisplayName,
    Guid Version);

public sealed record AssetAttachmentDownload(
    string FileName,
    string ContentType,
    byte[] Content,
    string Sha256);

public sealed record AssetAttachmentStorageUsage(
    long AttachmentCount,
    long StoredBytes);
