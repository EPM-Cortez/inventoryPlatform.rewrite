namespace Inventory.Contracts.V1;

public enum AttachmentCategoryContract
{
    Invoice,
    WarrantyDocument,
    DisposalCertificate,
    Photo,
    Other,
}

public sealed class ArchiveAssetAttachmentRequest
{
    public Guid Version { get; init; }
}

public sealed record AssetAttachmentResponse(
    Guid Id,
    Guid AssetId,
    AttachmentCategoryContract Category,
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
