using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public enum LeaverCaseStatusContract
{
    Open,
    Completed,
    Cancelled,
    Rehired,
}

public sealed class CreateLeaverCaseRequest
{
    public Guid PersonId { get; init; }

    public DateOnly LeavingOn { get; init; }

    [StringLength(2000)]
    public string? Notes { get; init; }
}

public sealed class UpdateLeaverCaseRequest
{
    public DateOnly LeavingOn { get; init; }

    [StringLength(2000)]
    public string? Notes { get; init; }

    public Guid Version { get; init; }
}

public sealed class ChangeLeaverCaseStatusRequest
{
    public Guid Version { get; init; }
}

public sealed record LeaverCustodyAssetResponse(
    Guid AssetId,
    string AssetTag,
    string? DeviceName,
    Guid AssignmentId,
    DateTimeOffset AssignedAtUtc,
    DateOnly? ExpectedReturnOn,
    bool IsOverdue,
    Guid AssignmentVersion,
    Guid AssetVersion);

public sealed record LeaverCaseResponse(
    Guid Id,
    Guid PersonId,
    string? PersonExternalId,
    string PersonDisplayName,
    bool PersonIsActive,
    DateOnly LeavingOn,
    string? Notes,
    LeaverCaseStatusContract Status,
    IReadOnlyList<LeaverCustodyAssetResponse> CurrentAssets,
    int CurrentAssetCount,
    bool CurrentAssetsTruncated,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    string CreatedByDisplayName,
    DateTimeOffset? CompletedAtUtc,
    Guid? CompletedByUserId,
    string? CompletedByDisplayName,
    DateTimeOffset? CancelledAtUtc,
    Guid? CancelledByUserId,
    string? CancelledByDisplayName,
    Guid Version);

public sealed record LeaverCaseSummaryResponse(
    Guid Id,
    Guid PersonId,
    string? PersonExternalId,
    string PersonDisplayName,
    bool PersonIsActive,
    DateOnly LeavingOn,
    LeaverCaseStatusContract Status,
    int CurrentAssetCount,
    int OverdueAssetCount,
    Guid Version);

public sealed record CustodyExceptionResponse(
    Guid PersonId,
    string? PersonExternalId,
    string PersonDisplayName,
    bool PersonIsActive,
    Guid? LeaverCaseId,
    DateOnly? LeavingOn,
    int CurrentAssetCount,
    int OverdueAssetCount);
