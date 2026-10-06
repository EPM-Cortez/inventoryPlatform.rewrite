using Inventory.Domain.Assets;
using Inventory.Domain.BulkChanges;

namespace Inventory.Application.BulkChanges;

public sealed record AssetBulkChangeSelection(Guid AssetId, Guid Version);

public sealed record AssetBulkChangeOperation(
    AssetBulkChangeOperationKind Kind,
    Guid? LocationId = null,
    bool ClearLocation = false,
    Guid? CategoryId = null,
    AssetCondition? Condition = null,
    AssetStatus? Status = null,
    string? Note = null);

public sealed record PreviewAssetBulkChangeCommand(
    IReadOnlyCollection<AssetBulkChangeSelection> Assets,
    AssetBulkChangeOperation Operation,
    string Reason);

public sealed record CommitAssetBulkChangeCommand(
    Guid JobId,
    string Fingerprint,
    Guid Version);

public sealed record AssetBulkChangePreviewItem(
    Guid AssetId,
    string AssetTag,
    Guid Version,
    string Field,
    string? From,
    string? To,
    bool IsNoOp);

public sealed record AssetBulkChangePreviewView(
    Guid JobId,
    AssetBulkChangeJobStatus Status,
    AssetBulkChangeOperationKind Operation,
    string Reason,
    int AssetCount,
    IReadOnlyList<AssetBulkChangePreviewItem> Items,
    string Fingerprint,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? CommittedAtUtc,
    Guid Version);

public sealed record AssetBulkChangeCommitItem(
    Guid AssetId,
    string AssetTag,
    Guid Version);

public sealed record AssetBulkChangeCommitView(
    Guid JobId,
    AssetBulkChangeJobStatus Status,
    int AssetCount,
    DateTimeOffset CommittedAtUtc,
    IReadOnlyList<AssetBulkChangeCommitItem> Assets,
    Guid Version);
