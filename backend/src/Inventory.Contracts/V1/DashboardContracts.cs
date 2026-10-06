namespace Inventory.Contracts.V1;

public sealed record DashboardValueCountResponse<T>(T Value, long Count);

public sealed record DashboardCategoryCountResponse(
    Guid CategoryId,
    string CategoryCode,
    string CategoryName,
    long Count);

public sealed record DashboardExceptionCountResponse(
    string Code,
    string Label,
    long Count);

public sealed record DashboardSummaryResponse(
    long TotalAssets,
    long ActiveAssets,
    IReadOnlyList<DashboardValueCountResponse<AssetStatusContract>> StatusCounts,
    IReadOnlyList<DashboardValueCountResponse<AssetConditionContract>> ConditionCounts,
    IReadOnlyList<DashboardCategoryCountResponse> CategoryCounts,
    bool CategoryCountsTruncated,
    IReadOnlyList<DashboardExceptionCountResponse> ExceptionCounts);

public sealed record InventoryLocationCountResponse(
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

public sealed record InventoryDistributionResponse(
    PagedResponse<InventoryLocationCountResponse> Groups,
    long MatchingAssetCount);

public sealed record DashboardExceptionAssetResponse(
    Guid AssetId,
    string AssetTag,
    string? DeviceName,
    ReferenceSummaryResponse Category,
    AssetStatusContract Status,
    AssetConditionContract Condition,
    LocationSummaryResponse? Location,
    DateTimeOffset UpdatedAtUtc);
