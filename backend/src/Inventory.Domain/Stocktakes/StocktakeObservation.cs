using Inventory.Domain.Assets;
using Inventory.Domain.Common;

namespace Inventory.Domain.Stocktakes;

public sealed class StocktakeObservation
{
    private StocktakeObservation()
    {
    }

    private StocktakeObservation(
        Guid id,
        Guid sessionId,
        Guid assetId,
        string assetTag,
        string? deviceName,
        Guid categoryId,
        string categoryCode,
        string categoryName,
        Guid observedLocationId,
        string observedLocationCode,
        string observedLocationName,
        AssetCondition observedCondition,
        string? note,
        DateTimeOffset observedAtUtc,
        Guid observedByUserId,
        string observedByDisplayName)
    {
        if (sessionId == Guid.Empty || assetId == Guid.Empty || categoryId == Guid.Empty)
        {
            throw new DomainRuleException(
                "stocktake_observation_reference_required",
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
        SetObservation(
            observedLocationId,
            observedLocationCode,
            observedLocationName,
            observedCondition,
            note,
            observedAtUtc,
            observedByUserId,
            observedByDisplayName);
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public Guid AssetId { get; private set; }

    public string AssetTag { get; private set; } = string.Empty;

    public string? DeviceName { get; private set; }

    public Guid CategoryId { get; private set; }

    public string CategoryCode { get; private set; } = string.Empty;

    public string CategoryName { get; private set; } = string.Empty;

    public Guid ObservedLocationId { get; private set; }

    public string ObservedLocationCode { get; private set; } = string.Empty;

    public string ObservedLocationName { get; private set; } = string.Empty;

    public AssetCondition ObservedCondition { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset ObservedAtUtc { get; private set; }

    public Guid ObservedByUserId { get; private set; }

    public string ObservedByDisplayName { get; private set; } = string.Empty;

    public static StocktakeObservation Record(
        Guid sessionId,
        Guid assetId,
        string assetTag,
        string? deviceName,
        Guid categoryId,
        string categoryCode,
        string categoryName,
        Guid observedLocationId,
        string observedLocationCode,
        string observedLocationName,
        AssetCondition observedCondition,
        string? note,
        DateTimeOffset observedAtUtc,
        Guid observedByUserId,
        string observedByDisplayName) =>
        new(
            Guid.NewGuid(),
            sessionId,
            assetId,
            assetTag,
            deviceName,
            categoryId,
            categoryCode,
            categoryName,
            observedLocationId,
            observedLocationCode,
            observedLocationName,
            observedCondition,
            note,
            observedAtUtc,
            observedByUserId,
            observedByDisplayName);

    public bool Matches(
        Guid observedLocationId,
        AssetCondition observedCondition,
        string? note)
    {
        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return ObservedLocationId == observedLocationId &&
               ObservedCondition == observedCondition &&
               string.Equals(Note, normalizedNote, StringComparison.Ordinal);
    }

    public void Update(
        Guid observedLocationId,
        string observedLocationCode,
        string observedLocationName,
        AssetCondition observedCondition,
        string? note,
        DateTimeOffset observedAtUtc,
        Guid observedByUserId,
        string observedByDisplayName) =>
        SetObservation(
            observedLocationId,
            observedLocationCode,
            observedLocationName,
            observedCondition,
            note,
            observedAtUtc,
            observedByUserId,
            observedByDisplayName);

    private void SetObservation(
        Guid observedLocationId,
        string observedLocationCode,
        string observedLocationName,
        AssetCondition observedCondition,
        string? note,
        DateTimeOffset observedAtUtc,
        Guid observedByUserId,
        string observedByDisplayName)
    {
        if (observedLocationId == Guid.Empty)
        {
            throw new DomainRuleException(
                "observed_location_required",
                "An observed location is required.");
        }

        if (observedByUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "actor_required",
                "A stocktake observation must record who made it.");
        }

        if (observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                "Stocktake timestamps must be UTC.");
        }

        if (!Enum.IsDefined(observedCondition))
        {
            throw new DomainRuleException(
                "invalid_asset_condition",
                "The observed asset condition is not supported.");
        }

        ObservedLocationId = observedLocationId;
        ObservedLocationCode = TextRules.Required(
            observedLocationCode,
            nameof(ObservedLocationCode),
            50);
        ObservedLocationName = TextRules.Required(
            observedLocationName,
            nameof(ObservedLocationName),
            150);
        ObservedCondition = observedCondition;
        Note = TextRules.Optional(note, nameof(Note), 1000);
        ObservedAtUtc = observedAtUtc;
        ObservedByUserId = observedByUserId;
        ObservedByDisplayName = TextRules.Required(
            observedByDisplayName,
            nameof(ObservedByDisplayName),
            200);
    }
}
