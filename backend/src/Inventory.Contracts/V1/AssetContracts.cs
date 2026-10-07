using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public enum AssetStatusContract
{
    InStock,
    Reserved,
    Assigned,
    Repair,
    Lost,
    Stolen,
    Retired,
    Disposed,
}

public enum AssetConditionContract
{
    New,
    Good,
    Fair,
    Damaged,
    Unserviceable,
}

public enum WarrantyStateContract
{
    NotRecorded,
    Future,
    Active,
    ExpiringSoon,
    Expired,
}

public abstract class AssetDetailsRequest
{
    [StringLength(150)]
    public string? DeviceName { get; init; }

    public Guid CategoryId { get; init; }

    [StringLength(2000)]
    public string? Description { get; init; }

    [StringLength(150)]
    public string? Manufacturer { get; init; }

    [StringLength(150)]
    public string? Model { get; init; }

    [StringLength(200)]
    public string? SerialNumber { get; init; }

    [StringLength(17)]
    public string? LanMacAddress { get; init; }

    public Guid? BiosGuid { get; init; }

    [StringLength(100)]
    public string? Ownership { get; init; }

    public AssetStatusContract Status { get; init; } = AssetStatusContract.InStock;

    public AssetConditionContract Condition { get; init; } = AssetConditionContract.Good;

    public Guid? LocationId { get; init; }

    public DateOnly? PurchaseDate { get; init; }

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal? PurchaseCost { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string? Currency { get; init; }

    [StringLength(200)]
    public string? Supplier { get; init; }

    [StringLength(100)]
    public string? OperatingSystem { get; init; }

    [StringLength(100)]
    public string? OperatingSystemVersion { get; init; }

    public bool? Windows11Compliant { get; init; }

    [StringLength(4000)]
    public string? Notes { get; init; }

    [StringLength(200)]
    public string? LegacyId { get; init; }
}

public class CreateAssetRequest : AssetDetailsRequest, IValidatableObject
{
    [StringLength(100)]
    public string? AssetTag { get; init; }

    public bool GenerateAssetTag { get; init; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        var hasAssetTag = !string.IsNullOrWhiteSpace(AssetTag);
        if (hasAssetTag == GenerateAssetTag)
        {
            yield return new ValidationResult(
                "Supply assetTag or set generateAssetTag to true, but not both.",
                [nameof(AssetTag), nameof(GenerateAssetTag)]);
        }
    }
}

public sealed class UpdateAssetRequest : AssetDetailsRequest
{
    [Required]
    [StringLength(100)]
    public string AssetTag { get; init; } = string.Empty;

    public Guid Version { get; init; }
}

public sealed class RetireAssetRequest
{
    public Guid Version { get; init; }

    [Required]
    [StringLength(1000)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class DisposeAssetRequest
{
    public Guid Version { get; init; }

    [Required]
    [StringLength(1000)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class AddAssetNoteRequest
{
    [Required]
    [StringLength(4000)]
    public string Note { get; init; } = string.Empty;
}

public sealed record AssetListItemResponse(
    Guid Id,
    string AssetTag,
    string? DeviceName,
    ReferenceSummaryResponse Category,
    string? Description,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? LanMacAddress,
    Guid? BiosGuid,
    string? Ownership,
    AssetStatusContract Status,
    AssetConditionContract Condition,
    LocationSummaryResponse? Location,
    CurrentAssignmentResponse? CurrentAssignment,
    WarrantySummaryResponse WarrantySummary,
    string? OperatingSystem,
    string? OperatingSystemVersion,
    bool? Windows11Compliant,
    string QrCodeUrl,
    DateTimeOffset UpdatedAtUtc,
    Guid Version);

public sealed record AssetResponse(
    Guid Id,
    string AssetTag,
    string? DeviceName,
    ReferenceSummaryResponse Category,
    string? Description,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? LanMacAddress,
    Guid? BiosGuid,
    string? Ownership,
    AssetStatusContract Status,
    AssetConditionContract Condition,
    LocationSummaryResponse? Location,
    CurrentAssignmentResponse? CurrentAssignment,
    WarrantySummaryResponse WarrantySummary,
    string? OperatingSystem,
    string? OperatingSystemVersion,
    bool? Windows11Compliant,
    string QrCodeUrl,
    DateOnly? PurchaseDate,
    decimal? PurchaseCost,
    string? Currency,
    string? Supplier,
    string? Notes,
    string? LegacyId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? RetiredAtUtc,
    string? RetirementReason,
    DateTimeOffset? DisposedAtUtc,
    string? DisposalReason,
    Guid Version,
    string? ReportedSerialNumber = null);

public sealed record WarrantySummaryResponse(
    WarrantyStateContract State,
    string? Provider,
    DateOnly? EndsOn,
    int? DaysRemaining);

public sealed record CurrentAssignmentResponse(
    Guid AssignmentId,
    AssignmentTargetResponse Target,
    DateTimeOffset AssignedAtUtc,
    Guid Version,
    DateOnly? ExpectedReturnOn,
    bool IsOverdue);

public sealed record AssetEventResponse(
    Guid Id,
    string Type,
    DateTimeOffset OccurredAtUtc,
    Guid ActorUserId,
    string ActorDisplayName,
    string Source,
    string Summary,
    object Details,
    string? CorrelationId);
