using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public sealed record AssetTagPolicyResponse(
    string Prefix,
    int NumericWidth,
    long NextNumber,
    string? NextAssetTag,
    Guid Version);

public sealed class UpdateAssetTagPolicyRequest
{
    [Required]
    [StringLength(50)]
    public string Prefix { get; init; } = string.Empty;

    [Range(2, 12)]
    public int NumericWidth { get; init; }

    [Range(1, 999_999_999_999)]
    public long NextNumber { get; init; }

    public Guid Version { get; init; }
}

public enum AssetBulkChangeOperationContract
{
    SetLocation,
    SetCategory,
    SetCondition,
    SetStatus,
    AddNote,
}

public enum AssetBulkChangeJobStatusContract
{
    Previewed,
    Committed,
}

public sealed record AssetBulkChangeSelectionRequest(
    Guid AssetId,
    Guid Version);

public sealed class AssetBulkChangeOperationRequest
{
    public AssetBulkChangeOperationContract Kind { get; init; }

    public Guid? LocationId { get; init; }

    public bool ClearLocation { get; init; }

    public Guid? CategoryId { get; init; }

    public AssetConditionContract? Condition { get; init; }

    public AssetStatusContract? Status { get; init; }

    [StringLength(4000)]
    public string? Note { get; init; }
}

public sealed class PreviewAssetBulkChangeRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(200)]
    public IReadOnlyList<AssetBulkChangeSelectionRequest> Assets { get; init; } = [];

    [Required]
    public AssetBulkChangeOperationRequest Operation { get; init; } = new();

    [Required]
    [StringLength(1000)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class CommitAssetBulkChangeRequest
{
    [Required]
    [StringLength(100)]
    public string Fingerprint { get; init; } = string.Empty;

    public Guid Version { get; init; }
}

public sealed record AssetBulkChangePreviewItemResponse(
    Guid AssetId,
    string AssetTag,
    Guid Version,
    string Field,
    string? From,
    string? To,
    bool IsNoOp);

public sealed record AssetBulkChangePreviewResponse(
    Guid JobId,
    AssetBulkChangeJobStatusContract Status,
    AssetBulkChangeOperationContract Operation,
    string Reason,
    int AssetCount,
    IReadOnlyList<AssetBulkChangePreviewItemResponse> Items,
    string Fingerprint,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? CommittedAtUtc,
    Guid Version);

public sealed record AssetBulkChangeCommitItemResponse(
    Guid AssetId,
    string AssetTag,
    Guid Version);

public sealed record AssetBulkChangeCommitResponse(
    Guid JobId,
    AssetBulkChangeJobStatusContract Status,
    int AssetCount,
    DateTimeOffset CommittedAtUtc,
    IReadOnlyList<AssetBulkChangeCommitItemResponse> Assets,
    Guid Version);

public enum AssetBundleMemberRoleContract
{
    Laptop,
    Charger,
    Dock,
    Case,
    Other,
}

public sealed class AssetBundleMemberRequest
{
    public Guid AssetId { get; init; }

    public AssetBundleMemberRoleContract Role { get; init; }

    [StringLength(100)]
    public string? OtherRole { get; init; }
}

public class CreateAssetBundleRequest
{
    [Required]
    [StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Name { get; init; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; init; }

    [Required]
    [MinLength(1)]
    [MaxLength(50)]
    public IReadOnlyList<AssetBundleMemberRequest> Members { get; init; } = [];
}

public sealed class UpdateAssetBundleRequest : CreateAssetBundleRequest
{
    public Guid Version { get; init; }
}

public sealed record AssetBundleMemberResponse(
    Guid MemberId,
    Guid AssetId,
    string AssetTag,
    AssetBundleMemberRoleContract Role,
    string? OtherRole,
    DateTimeOffset AddedAtUtc);

public sealed record AssetBundleListItemResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int MemberCount,
    bool IsArchived,
    DateTimeOffset UpdatedAtUtc,
    Guid Version);

public sealed record AssetBundleResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<AssetBundleMemberResponse> Members,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ArchivedAtUtc,
    bool IsArchived,
    Guid Version);
