namespace Inventory.Application.Leavers;

public enum LeaverCaseStatusView
{
    Open,
    Completed,
    Cancelled,
    Rehired,
}

public sealed record CreateLeaverCaseCommand(
    Guid PersonId,
    DateOnly LeavingOn,
    string? Notes);

public sealed record UpdateLeaverCaseCommand(
    DateOnly LeavingOn,
    string? Notes,
    Guid Version);

public sealed record ChangeLeaverCaseStatusCommand(Guid Version);

public sealed record LeaverCaseSearch(
    string? Query,
    LeaverCaseStatusView? Status,
    int Page,
    int PageSize);

public sealed record LeaverCaseSummaryView(
    Guid Id,
    Guid PersonId,
    string? PersonExternalId,
    string PersonDisplayName,
    bool PersonIsActive,
    DateOnly LeavingOn,
    LeaverCaseStatusView Status,
    int CurrentAssetCount,
    int OverdueAssetCount,
    Guid Version);

public sealed record LeaverCustodyAssetView(
    Guid AssetId,
    string AssetTag,
    string? DeviceName,
    Guid AssignmentId,
    DateTimeOffset AssignedAtUtc,
    DateOnly? ExpectedReturnOn,
    bool IsOverdue,
    Guid AssignmentVersion,
    Guid AssetVersion);

public sealed record LeaverCaseDetailView(
    Guid Id,
    Guid PersonId,
    string? PersonExternalId,
    string PersonDisplayName,
    bool PersonIsActive,
    DateOnly LeavingOn,
    string? Notes,
    LeaverCaseStatusView Status,
    IReadOnlyList<LeaverCustodyAssetView> CurrentAssets,
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

public sealed record CustodyExceptionView(
    Guid PersonId,
    string? PersonExternalId,
    string PersonDisplayName,
    bool PersonIsActive,
    Guid? LeaverCaseId,
    DateOnly? LeavingOn,
    int CurrentAssetCount,
    int OverdueAssetCount);
