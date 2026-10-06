using Inventory.Domain.Common;

namespace Inventory.Domain.BulkChanges;

public enum AssetBulkChangeOperationKind
{
    SetLocation,
    SetCategory,
    SetCondition,
    SetStatus,
    AddNote,
}

public enum AssetBulkChangeJobStatus
{
    Previewed,
    Committed,
}

public sealed class AssetBulkChangeJob
{
    private AssetBulkChangeJob()
    {
    }

    private AssetBulkChangeJob(
        Guid id,
        AssetBulkChangeOperationKind operationKind,
        string operationJson,
        string itemsJson,
        string reason,
        string fingerprint,
        int assetCount,
        Guid createdByUserId,
        string createdByDisplayName,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        EnsureUtc(createdAtUtc);
        EnsureUtc(expiresAtUtc);
        if (expiresAtUtc <= createdAtUtc)
        {
            throw new DomainRuleException(
                "bulk_preview_expiry_invalid",
                "Bulk preview expiry must be after its creation time.");
        }

        if (assetCount is < 1 or > 200)
        {
            throw new DomainRuleException(
                "bulk_asset_count_invalid",
                "Bulk changes must contain between 1 and 200 assets.");
        }

        Id = id;
        OperationKind = operationKind;
        OperationJson = TextRules.Required(operationJson, nameof(OperationJson), 8000);
        ItemsJson = TextRules.Required(itemsJson, nameof(ItemsJson), 32000);
        Reason = TextRules.Required(reason, nameof(Reason), 1000);
        Fingerprint = TextRules.Required(fingerprint, nameof(Fingerprint), 100);
        AssetCount = assetCount;
        CreatedByUserId = createdByUserId;
        CreatedByDisplayName = TextRules.Required(
            createdByDisplayName,
            nameof(CreatedByDisplayName),
            200);
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Status = AssetBulkChangeJobStatus.Previewed;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public AssetBulkChangeOperationKind OperationKind { get; private set; }

    public string OperationJson { get; private set; } = "{}";

    public string ItemsJson { get; private set; } = "[]";

    public string Reason { get; private set; } = string.Empty;

    public string Fingerprint { get; private set; } = string.Empty;

    public int AssetCount { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public string CreatedByDisplayName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? CommittedAtUtc { get; private set; }

    public AssetBulkChangeJobStatus Status { get; private set; }

    public Guid Version { get; private set; }

    public static AssetBulkChangeJob Create(
        AssetBulkChangeOperationKind operationKind,
        string operationJson,
        string itemsJson,
        string reason,
        string fingerprint,
        int assetCount,
        Guid createdByUserId,
        string createdByDisplayName,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc) =>
        new(
            Guid.NewGuid(),
            operationKind,
            operationJson,
            itemsJson,
            reason,
            fingerprint,
            assetCount,
            createdByUserId,
            createdByDisplayName,
            createdAtUtc,
            expiresAtUtc);

    public void Commit(
        Guid expectedVersion,
        string expectedFingerprint,
        DateTimeOffset committedAtUtc)
    {
        EnsureUtc(committedAtUtc);
        EnsureVersion(expectedVersion);
        if (!string.Equals(
                Fingerprint,
                expectedFingerprint?.Trim(),
                StringComparison.Ordinal))
        {
            throw new DomainRuleException(
                "bulk_preview_fingerprint_mismatch",
                "The bulk preview fingerprint does not match.");
        }

        if (Status == AssetBulkChangeJobStatus.Committed)
        {
            throw new DomainRuleException(
                "bulk_preview_already_committed",
                "The bulk preview was already committed.");
        }

        if (committedAtUtc >= ExpiresAtUtc)
        {
            throw new DomainRuleException(
                "bulk_preview_expired",
                "The bulk preview has expired. Create a new preview.");
        }

        Status = AssetBulkChangeJobStatus.Committed;
        CommittedAtUtc = committedAtUtc;
        Version = Guid.NewGuid();
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The bulk preview was changed by another request.");
        }
    }

    private static void EnsureUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                "Bulk change timestamps must be UTC.");
        }
    }
}
