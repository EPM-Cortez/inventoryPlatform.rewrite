using Inventory.Application.Common;
using Inventory.Domain.Assets;

namespace Inventory.Application.Dashboard;

public static class DashboardExceptionCodes
{
    public const string MissingLocation = "missingLocation";
    public const string MissingSerialNumber = "missingSerialNumber";
    public const string Repair = "repair";
    public const string LostOrStolen = "lostOrStolen";
    public const string AssignedWithoutCustody = "assignedWithoutCustody";
    public const string InactivePersonCustody = "inactivePersonCustody";
    public const string MissingWarranty = "missingWarranty";
    public const string ExpiredWarranty = "expiredWarranty";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [
            MissingLocation,
            MissingSerialNumber,
            Repair,
            LostOrStolen,
            AssignedWithoutCustody,
            InactivePersonCustody,
            MissingWarranty,
            ExpiredWarranty,
        ],
        StringComparer.Ordinal);
}

public sealed record DashboardCount<T>(T Value, long Count);

public sealed record DashboardCategoryCount(
    Guid CategoryId,
    string CategoryCode,
    string CategoryName,
    long Count);

public sealed record DashboardExceptionCount(
    string Code,
    string Label,
    long Count);

public sealed record DashboardSummaryView(
    long TotalAssets,
    long ActiveAssets,
    IReadOnlyList<DashboardCount<AssetStatus>> StatusCounts,
    IReadOnlyList<DashboardCount<AssetCondition>> ConditionCounts,
    IReadOnlyList<DashboardCategoryCount> CategoryCounts,
    bool CategoryCountsTruncated,
    IReadOnlyList<DashboardExceptionCount> ExceptionCounts);

public sealed record InventoryDistributionSearch(
    Guid? CategoryId,
    string? Query,
    bool IncludeTerminalAssets,
    int Page,
    int PageSize);

public sealed record InventoryDistributionItem(
    Guid CategoryId,
    string CategoryCode,
    string CategoryName,
    Guid? LocationId,
    string? LocationCode,
    string? LocationName,
    long Quantity,
    long InStockQuantity,
    long AssignedQuantity,
    long OtherQuantity);

public sealed record InventoryDistributionView(
    PageResult<InventoryDistributionItem> Groups,
    long MatchingAssetCount);

public sealed record DashboardExceptionAssetView(
    Guid AssetId,
    string AssetTag,
    string? DeviceName,
    Guid CategoryId,
    string CategoryCode,
    string CategoryName,
    AssetStatus Status,
    AssetCondition Condition,
    Guid? LocationId,
    string? LocationCode,
    string? LocationName,
    DateTimeOffset UpdatedAtUtc);
