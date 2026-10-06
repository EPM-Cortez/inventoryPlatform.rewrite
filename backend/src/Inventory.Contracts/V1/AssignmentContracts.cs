using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public enum AssignmentTargetTypeContract
{
    Person,
    Department,
}

public sealed class AssignAssetRequest
{
    public Guid? PersonId { get; init; }

    public Guid? DepartmentId { get; init; }

    public Guid AssetVersion { get; init; }

    [StringLength(2000)]
    public string? Notes { get; init; }

    public DateOnly? ExpectedReturnOn { get; init; }
}

public sealed class ReturnAssetRequest
{
    public Guid AssetVersion { get; init; }

    public Guid AssignmentVersion { get; init; }

    public AssetConditionContract? Condition { get; init; }

    public Guid? LocationId { get; init; }

    [StringLength(2000)]
    public string? Notes { get; init; }
}

public sealed class UpdateExpectedReturnRequest
{
    public DateOnly? ExpectedReturnOn { get; init; }

    public Guid AssignmentVersion { get; init; }
}

public sealed record AssignmentTargetResponse(
    AssignmentTargetTypeContract Type,
    Guid Id,
    string DisplayName,
    string? Reference);

public sealed record AssetAssignmentResponse(
    Guid Id,
    Guid AssetId,
    AssignmentTargetResponse Target,
    DateTimeOffset AssignedAtUtc,
    Guid AssignedByUserId,
    string AssignedByDisplayName,
    AssetConditionContract AssignedCondition,
    LocationSummaryResponse? AssignedLocation,
    string? AssignmentNotes,
    DateTimeOffset? ReturnedAtUtc,
    Guid? ReturnedByUserId,
    string? ReturnedByDisplayName,
    AssetConditionContract? ReturnCondition,
    LocationSummaryResponse? ReturnLocation,
    string? ReturnNotes,
    Guid Version,
    DateOnly? ExpectedReturnOn = null,
    bool IsOverdue = false);

public sealed record AssignmentWorkflowResponse(
    AssetAssignmentResponse Assignment,
    AssetStatusContract AssetStatus,
    AssetConditionContract AssetCondition,
    LocationSummaryResponse? AssetLocation,
    Guid AssetVersion);

public sealed record HolderAssetResponse(
    Guid AssetId,
    string AssetTag,
    AssetStatusContract Status,
    AssetConditionContract Condition,
    LocationSummaryResponse? Location,
    Guid AssetVersion,
    AssetAssignmentResponse Assignment);

public sealed record OverdueLoanResponse(
    Guid AssetId,
    string AssetTag,
    string? DeviceName,
    Guid AssignmentId,
    AssignmentTargetResponse Target,
    DateTimeOffset AssignedAtUtc,
    DateOnly ExpectedReturnOn,
    int DaysOverdue,
    Guid AssignmentVersion,
    Guid AssetVersion);
