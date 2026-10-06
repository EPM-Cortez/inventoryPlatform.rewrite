using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public enum StocktakeScopeContract
{
    All,
    Location,
    Category,
}

public enum StocktakeStatusContract
{
    InProgress,
    Completed,
    Cancelled,
}

public enum StocktakeItemFilterContract
{
    All,
    Seen,
    Missing,
    Unexpected,
    WrongLocation,
    ConditionChanged,
}

public sealed class StartStocktakeRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    public StocktakeScopeContract Scope { get; init; }

    public Guid? LocationId { get; init; }

    public Guid? CategoryId { get; init; }
}

public sealed class RecordStocktakeObservationRequest
{
    public Guid? AssetId { get; init; }

    [StringLength(100)]
    public string? AssetTag { get; init; }

    public Guid ObservedLocationId { get; init; }

    public required AssetConditionContract ObservedCondition { get; init; }

    [StringLength(1000)]
    public string? Note { get; init; }

    public Guid Version { get; init; }
}

public sealed record StocktakeActionRequest(Guid Version);

public sealed class CancelStocktakeRequest
{
    public Guid Version { get; init; }

    [Required]
    [StringLength(1000)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record StocktakeReferenceResponse(
    Guid Id,
    string Code,
    string Name,
    bool IsActive);

public sealed record StocktakeProgressResponse(
    long Expected,
    long Seen,
    long Missing,
    long Unexpected,
    long WrongLocation,
    long ConditionChanged);

public sealed record StocktakeSessionResponse(
    Guid Id,
    string Name,
    StocktakeScopeContract Scope,
    StocktakeReferenceResponse? ScopeLocation,
    StocktakeReferenceResponse? ScopeCategory,
    StocktakeStatusContract Status,
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
    StocktakeProgressResponse Progress,
    Guid Version);

public sealed record StocktakeSessionListItemResponse(
    Guid Id,
    string Name,
    StocktakeScopeContract Scope,
    string ScopeDisplayName,
    StocktakeStatusContract Status,
    DateTimeOffset StartedAtUtc,
    string StartedByDisplayName,
    DateTimeOffset? ClosedAtUtc,
    StocktakeProgressResponse Progress,
    Guid Version);

public sealed record StocktakeItemResponse(
    Guid AssetId,
    string AssetTag,
    string? DeviceName,
    ReferenceSummaryResponse Category,
    bool IsExpected,
    bool IsSeen,
    AssetStatusContract? ExpectedStatus,
    AssetConditionContract? ExpectedCondition,
    LocationSummaryResponse? ExpectedLocation,
    LocationSummaryResponse? ObservedLocation,
    AssetConditionContract? ObservedCondition,
    string? Note,
    DateTimeOffset? ObservedAtUtc,
    Guid? ObservedByUserId,
    string? ObservedByDisplayName,
    bool IsUnexpected,
    bool IsWrongLocation,
    bool IsConditionChanged);

public sealed record StocktakeObservationResponse(
    StocktakeSessionResponse Session,
    StocktakeItemResponse Item,
    bool Changed);
