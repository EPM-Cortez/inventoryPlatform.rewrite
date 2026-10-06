using Inventory.Domain.Assets;
using Inventory.Domain.Common;

namespace Inventory.Domain.Stocktakes;

/// <summary>
/// The immutable register state captured when a stocktake starts.
/// </summary>
public sealed class StocktakeExpectedAsset
{
    private StocktakeExpectedAsset()
    {
    }

    private StocktakeExpectedAsset(
        Guid id,
        Guid sessionId,
        Guid assetId,
        string assetTag,
        string? deviceName,
        Guid categoryId,
        string categoryCode,
        string categoryName,
        AssetStatus expectedStatus,
        AssetCondition expectedCondition,
        Guid? expectedLocationId,
        string? expectedLocationCode,
        string? expectedLocationName)
    {
        if (sessionId == Guid.Empty || assetId == Guid.Empty || categoryId == Guid.Empty)
        {
            throw new DomainRuleException(
                "stocktake_snapshot_reference_required",
                "The stocktake, asset and category identifiers are required.");
        }

        Id = id;
        SessionId = sessionId;
        AssetId = assetId;
        AssetTag = TextRules.Required(assetTag, nameof(AssetTag), 100);
        DeviceName = TextRules.Optional(deviceName, nameof(DeviceName), 150);
        CategoryId = categoryId;
        CategoryCode = TextRules.Required(categoryCode, nameof(CategoryCode), 50);
        CategoryName = TextRules.Required(categoryName, nameof(CategoryName), 150);
        ExpectedStatus = expectedStatus;
        ExpectedCondition = expectedCondition;
        ExpectedLocationId = expectedLocationId;
        ExpectedLocationCode = TextRules.Optional(
            expectedLocationCode,
            nameof(ExpectedLocationCode),
            50);
        ExpectedLocationName = TextRules.Optional(
            expectedLocationName,
            nameof(ExpectedLocationName),
            150);
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid AssetId { get; private set; }

    public string AssetTag { get; private set; } = string.Empty;

    public string? DeviceName { get; private set; }

    public Guid CategoryId { get; private set; }

    public string CategoryCode { get; private set; } = string.Empty;

    public string CategoryName { get; private set; } = string.Empty;

    public AssetStatus ExpectedStatus { get; private set; }

    public AssetCondition ExpectedCondition { get; private set; }

    public Guid? ExpectedLocationId { get; private set; }

    public string? ExpectedLocationCode { get; private set; }

    public string? ExpectedLocationName { get; private set; }

    public static StocktakeExpectedAsset Capture(
        Guid sessionId,
        Guid assetId,
        string assetTag,
        string? deviceName,
        Guid categoryId,
        string categoryCode,
        string categoryName,
        AssetStatus expectedStatus,
        AssetCondition expectedCondition,
        Guid? expectedLocationId,
        string? expectedLocationCode,
        string? expectedLocationName) =>
        new(
            Guid.NewGuid(),
            sessionId,
            assetId,
            assetTag,
            deviceName,
            categoryId,
            categoryCode,
            categoryName,
            expectedStatus,
            expectedCondition,
            expectedLocationId,
            expectedLocationCode,
            expectedLocationName);
}
