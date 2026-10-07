namespace Inventory.Contracts.V1;

public enum AssetImportModeContract
{
    CreateOnly,
    UpsertByAssetTag,
}

public enum AssetImportStatusContract
{
    ValidationFailed,
    ReadyToCommit,
    Committed,
}

public enum AssetImportActionContract
{
    Create,
    Update,
}

public enum AssetImportAssignmentResolutionContract
{
    None,
    ExactPerson,
    FuzzyPerson,
    Department,
    Unassigned,
}

public enum AssetImportSerialResolutionContract
{
    Missing,
    Verified,
    ReportedDuplicate,
    IgnoredPlaceholder,
}

public sealed class CommitAssetImportRequest
{
    public Guid Version { get; init; }
}

public sealed record AssetImportErrorResponse(
    int RowNumber,
    string Column,
    string Code,
    string Message,
    string? Value);

public sealed record AssetImportPreviewRowResponse(
    int RowNumber,
    AssetImportActionContract Action,
    string AssetTag,
    bool AssetTagGenerated,
    string Category,
    string ResolvedCategoryCode,
    bool CategoryWillBeCreated,
    AssetStatusContract Status,
    AssetConditionContract Condition,
    string? LocationCode,
    string? ResolvedLocationCode,
    bool LocationWillBeCreated,
    string? AssignmentSource,
    AssetImportAssignmentResolutionContract AssignmentResolution,
    string? AssignedTo,
    DateOnly? PurchaseDate,
    DateOnly? WarrantyStartDate,
    DateOnly? WarrantyEndDate,
    IReadOnlyList<string> InferredDateFields,
    string? SourceSerialNumber = null,
    AssetImportSerialResolutionContract SerialResolution =
        AssetImportSerialResolutionContract.Missing);

public sealed record AssetImportJobResponse(
    Guid Id,
    int SchemaVersion,
    string OriginalFileName,
    string FileHash,
    AssetImportModeContract Mode,
    AssetImportStatusContract Status,
    int TotalRows,
    int ValidRows,
    int ErrorRows,
    int CreateRows,
    int UpdateRows,
    int GeneratedAssetTagRows,
    int PlannedCategoryCreateCount,
    int PlannedLocationCreateCount,
    int InferredDateRows,
    int FuzzyPersonMatchRows,
    int UnassignedImportRows,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    string CreatedByDisplayName,
    DateTimeOffset? CommittedAtUtc,
    Guid? CommittedByUserId,
    string? CommittedByDisplayName,
    int CommittedCreateRows,
    int CommittedUpdateRows,
    int CommittedUnchangedRows,
    IReadOnlyList<AssetImportPreviewRowResponse> Preview,
    IReadOnlyList<AssetImportErrorResponse> Errors,
    bool PreviewTruncated,
    bool ErrorsTruncated,
    Guid Version,
    int ReportedDuplicateSerialRows = 0);

public sealed record AssetImportCommitResponse(
    Guid ImportJobId,
    AssetImportStatusContract Status,
    int CreatedRows,
    int UpdatedRows,
    int UnchangedRows,
    int CreatedCategoryCount,
    int CreatedLocationCount,
    DateTimeOffset CommittedAtUtc,
    Guid Version);
