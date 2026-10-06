using Inventory.Domain.Bundles;

namespace Inventory.Application.Bundles;

public sealed record AssetBundleMemberCommand(
    Guid AssetId,
    AssetBundleMemberRole Role,
    string? OtherRole);

public sealed record CreateAssetBundleCommand(
    string Code,
    string Name,
    string? Description,
    IReadOnlyCollection<AssetBundleMemberCommand> Members);

public sealed record UpdateAssetBundleCommand(
    string Code,
    string Name,
    string? Description,
    IReadOnlyCollection<AssetBundleMemberCommand> Members,
    Guid Version);

public sealed record AssetBundleMemberView(
    Guid MemberId,
    Guid AssetId,
    string AssetTag,
    AssetBundleMemberRole Role,
    string? OtherRole,
    DateTimeOffset AddedAtUtc);

public sealed record AssetBundleListItem(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int MemberCount,
    bool IsArchived,
    DateTimeOffset UpdatedAtUtc,
    Guid Version);

public sealed record AssetBundleView(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<AssetBundleMemberView> Members,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ArchivedAtUtc,
    Guid Version);
