using System.Security.Cryptography;
using Inventory.Domain.Common;

namespace Inventory.Domain.Assets;

public enum AssetAttachmentCategory
{
    Invoice,
    WarrantyDocument,
    DisposalCertificate,
    Photo,
    Other,
}

public sealed class AssetAttachment
{
    public const int MaximumContentBytes = 10 * 1024 * 1024;
    public const int MaximumAttachmentsPerAsset = 100;
    public const long MaximumStoredBytesPerAsset = 250L * 1024 * 1024;

    private AssetAttachment()
    {
    }

    private AssetAttachment(
        Guid id,
        Guid assetId,
        AssetAttachmentCategory category,
        string fileName,
        string contentType,
        byte[] content,
        string sha256,
        DateTimeOffset createdAtUtc,
        Guid createdByUserId,
        string createdByDisplayName)
    {
        if (assetId == Guid.Empty)
        {
            throw new DomainRuleException(
                "asset_required",
                "An attachment must identify an asset.");
        }

        if (!Enum.IsDefined(category))
        {
            throw new DomainRuleException(
                "invalid_attachment_category",
                "The attachment category is not supported.");
        }

        if (content is not { Length: > 0 } ||
            content.Length > MaximumContentBytes)
        {
            throw new DomainRuleException(
                "invalid_attachment_size",
                $"Attachment content must contain 1 to {MaximumContentBytes} bytes.");
        }

        ValidateUtc(createdAtUtc);
        ValidateActor(createdByUserId);
        Id = id;
        AssetId = assetId;
        Category = category;
        FileName = TextRules.Required(fileName, nameof(FileName), 255);
        ContentType = TextRules.Required(contentType, nameof(ContentType), 100);
        Content = content.ToArray();
        SizeBytes = Content.LongLength;
        Sha256 = TextRules.Required(sha256, nameof(Sha256), 64);
        if (Sha256.Length != 64 ||
            Sha256.Any(character => !Uri.IsHexDigit(character)) ||
            !string.Equals(
                Sha256,
                Convert.ToHexString(SHA256.HashData(Content)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleException(
                "invalid_attachment_hash",
                "The attachment SHA-256 hash does not match its content.");
        }
        CreatedAtUtc = createdAtUtc;
        CreatedByUserId = createdByUserId;
        CreatedByDisplayName =
            TextRules.Required(createdByDisplayName, nameof(CreatedByDisplayName), 200);
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid AssetId { get; private set; }

    public AssetAttachmentCategory Category { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public byte[] Content { get; private set; } = [];

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public string CreatedByDisplayName { get; private set; } = string.Empty;

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public Guid? ArchivedByUserId { get; private set; }

    public string? ArchivedByDisplayName { get; private set; }

    public Guid Version { get; private set; }

    public static AssetAttachment Create(
        Guid assetId,
        AssetAttachmentCategory category,
        string fileName,
        string contentType,
        byte[] content,
        string sha256,
        DateTimeOffset createdAtUtc,
        Guid createdByUserId,
        string createdByDisplayName) =>
        new(
            Guid.NewGuid(),
            assetId,
            category,
            fileName,
            contentType,
            content,
            sha256,
            createdAtUtc,
            createdByUserId,
            createdByDisplayName);

    public void Archive(
        Guid expectedVersion,
        DateTimeOffset archivedAtUtc,
        Guid archivedByUserId,
        string archivedByDisplayName)
    {
        EnsureVersion(expectedVersion);
        if (ArchivedAtUtc.HasValue)
        {
            throw new DomainRuleException(
                "attachment_already_archived",
                "The attachment is already archived.");
        }

        ValidateUtc(archivedAtUtc);
        ValidateActor(archivedByUserId);
        ArchivedAtUtc = archivedAtUtc;
        ArchivedByUserId = archivedByUserId;
        ArchivedByDisplayName =
            TextRules.Required(
                archivedByDisplayName,
                nameof(ArchivedByDisplayName),
                200);
        Version = Guid.NewGuid();
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The attachment was changed by another user.");
        }
    }

    private static void ValidateActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "actor_required",
                "An attachment actor is required.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                "Attachment timestamps must be UTC.");
        }
    }
}
