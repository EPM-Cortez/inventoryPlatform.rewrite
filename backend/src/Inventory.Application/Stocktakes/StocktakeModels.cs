using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Stocktakes;

namespace Inventory.Application.Stocktakes;

public enum StocktakeItemFilter
{
    All,
    Seen,
    Missing,
    Unexpected,
    WrongLocation,
    ConditionChanged,
}

public sealed record StartStocktakeCommand(
    string Name,
    StocktakeScope Scope,
    Guid? LocationId,
    Guid? CategoryId);

public sealed record RecordStocktakeObservationCommand(
    Guid? AssetId,
    string? AssetTag,
    Guid ObservedLocationId,
    AssetCondition? ObservedCondition,
    string? Note,
    Guid Version);

public sealed record StocktakeAssetSnapshotSource(
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
    string? LocationName);

public sealed record StocktakeReferenceView(Guid Id, string Code, string Name, bool IsActive);

public sealed record StocktakeProgress(
    long Expected,
    long Seen,
    long Missing,
    long Unexpected,
    long WrongLocation,
    long ConditionChanged);

public sealed record StocktakeSessionView(
    Guid Id,
    string Name,
    StocktakeScope Scope,
    StocktakeReferenceView? ScopeLocation,
    StocktakeReferenceView? ScopeCategory,
    StocktakeStatus Status,
    DateTimeOffset StartedAtUtc,
    Guid StartedByUserId,
    string StartedByDisplayName,
    DateTimeOffset? CompletedAtUtc,
    Guid? CompletedByUserId,
    string? CompletedByDisplayName,
    DateTimeOffset? CancelledAtUtc,
    Guid? CancelledByUserId,
    string? CancelledByDisplayName,
    string? CancellationReason,
    StocktakeProgress Progress,
    Guid Version);

public sealed record StocktakeSessionListItem(
    Guid Id,
    string Name,
    StocktakeScope Scope,
    string ScopeDisplayName,
    StocktakeStatus Status,
    DateTimeOffset StartedAtUtc,
    string StartedByDisplayName,
    DateTimeOffset? ClosedAtUtc,
    StocktakeProgress Progress,
    Guid Version);

public sealed record StocktakeItemView(
    Guid AssetId,
    string AssetTag,
    string? DeviceName,
    Guid CategoryId,
    string CategoryCode,
    string CategoryName,
    bool IsExpected,
    bool IsSeen,
    AssetStatus? ExpectedStatus,
    AssetCondition? ExpectedCondition,
    Guid? ExpectedLocationId,
    string? ExpectedLocationCode,
    string? ExpectedLocationName,
    Guid? ObservedLocationId,
    string? ObservedLocationCode,
    string? ObservedLocationName,
    AssetCondition? ObservedCondition,
    string? Note,
    DateTimeOffset? ObservedAtUtc,
    Guid? ObservedByUserId,
    string? ObservedByDisplayName,
    bool IsUnexpected,
    bool IsWrongLocation,
    bool IsConditionChanged);

public sealed record StocktakeObservationResult(
    StocktakeSessionView Session,
    StocktakeItemView Item,
    bool Changed);

public sealed record StocktakeSessionSearch(
    StocktakeStatus? Status,
    int Page,
    int PageSize);

public sealed record StocktakeItemSearch(
    StocktakeItemFilter Filter,
    int Page,
    int PageSize);
