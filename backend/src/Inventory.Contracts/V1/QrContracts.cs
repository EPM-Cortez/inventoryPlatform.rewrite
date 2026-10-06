namespace Inventory.Contracts.V1;

public enum QrImageFormatContract
{
    Svg,
    Png,
}

public sealed record QrIdentifierResponse(
    Guid Id,
    Guid AssetId,
    int TokenVersion,
    DateTimeOffset IssuedAtUtc,
    string PayloadUrl,
    string ImageUrl);

public sealed record QrResolutionResponse(
    Guid Id,
    string AssetTag,
    ReferenceSummaryResponse Category,
    AssetStatusContract Status,
    AssetConditionContract Condition,
    LocationSummaryResponse? Location,
    WarrantySummaryResponse WarrantySummary);
