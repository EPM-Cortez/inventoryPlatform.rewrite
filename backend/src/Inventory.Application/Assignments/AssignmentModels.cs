using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;

namespace Inventory.Application.Assignments;

public sealed record AssignAssetCommand(
    Guid? PersonId,
    Guid? DepartmentId,
    Guid AssetVersion,
    string? Notes,
    DateOnly? ExpectedReturnOn = null);

public sealed record ReturnAssetCommand(
    Guid AssetVersion,
    Guid AssignmentVersion,
    AssetCondition? Condition,
    Guid? LocationId,
    string? Notes);

public sealed record UpdateExpectedReturnCommand(
    DateOnly? ExpectedReturnOn,
    Guid AssignmentVersion);

public sealed record AssetAssignmentView(
    Guid Id,
    Guid AssetId,
    AssignmentTargetType TargetType,
    Guid? PersonId,
    Guid? DepartmentId,
    string TargetDisplayName,
    string? TargetReference,
    DateTimeOffset AssignedAtUtc,
    Guid AssignedByUserId,
    string AssignedByDisplayName,
    AssetCondition AssignedCondition,
    Guid? AssignedLocationId,
    string? AssignedLocationCode,
    string? AssignedLocationName,
    string? AssignmentNotes,
    DateTimeOffset? ReturnedAtUtc,
    Guid? ReturnedByUserId,
    string? ReturnedByDisplayName,
    AssetCondition? ReturnCondition,
    Guid? ReturnLocationId,
    string? ReturnLocationCode,
    string? ReturnLocationName,
    string? ReturnNotes,
    Guid Version,
    DateOnly? ExpectedReturnOn = null,
    bool IsOverdue = false);

public sealed record AssignmentWorkflowResult(
    AssetAssignmentView Assignment,
    AssetStatus AssetStatus,
    AssetCondition AssetCondition,
    Guid? AssetLocationId,
    string? AssetLocationCode,
    string? AssetLocationName,
    Guid AssetVersion);

public sealed record HolderAssetView(
    Guid AssetId,
    string AssetTag,
    AssetStatus Status,
    AssetCondition Condition,
    Guid? LocationId,
    string? LocationCode,
    string? LocationName,
    Guid AssetVersion,
    AssetAssignmentView Assignment);

public sealed record OverdueLoanView(
    Guid AssetId,
    string AssetTag,
    string? DeviceName,
    Guid AssignmentId,
    AssignmentTargetType TargetType,
    Guid TargetId,
    string TargetDisplayName,
    string? TargetReference,
    DateTimeOffset AssignedAtUtc,
    DateOnly ExpectedReturnOn,
    int DaysOverdue,
    Guid AssignmentVersion,
    Guid AssetVersion);
