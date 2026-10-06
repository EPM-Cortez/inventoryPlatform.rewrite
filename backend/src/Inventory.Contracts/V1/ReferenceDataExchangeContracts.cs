namespace Inventory.Contracts.V1;

public enum ReferenceDataImportResourceContract
{
    People,
    Departments,
    Locations,
}

public enum ReferenceDataImportModeContract
{
    CreateOnly,
    UpsertByKey,
}

public enum ReferenceDataImportStatusContract
{
    ValidationFailed,
    ReadyToCommit,
    Committed,
}

public enum ReferenceDataImportActionContract
{
    Create,
    Update,
}

public sealed class CommitReferenceDataImportRequest
{
    public Guid Version { get; init; }
}

public sealed record ReferenceDataImportErrorResponse(
    int RowNumber,
    string Column,
    string Code,
    string Message,
    string? Value);

public sealed record ReferenceDataImportPreviewRowResponse(
    int RowNumber,
    ReferenceDataImportActionContract Action,
    string Key,
    bool KeyGenerated,
    string Name,
    string? Type,
    string? ParentKey,
    bool IsActive);

public sealed record ReferenceDataImportJobResponse(
    Guid Id,
    ReferenceDataImportResourceContract Resource,
    int SchemaVersion,
    string OriginalFileName,
    string FileHash,
    ReferenceDataImportModeContract Mode,
    ReferenceDataImportStatusContract Status,
    int TotalRows,
    int ValidRows,
    int ErrorRows,
    int CreateRows,
    int UpdateRows,
    int GeneratedKeyRows,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    string CreatedByDisplayName,
    DateTimeOffset? CommittedAtUtc,
    Guid? CommittedByUserId,
    string? CommittedByDisplayName,
    int CommittedCreateRows,
    int CommittedUpdateRows,
    int CommittedUnchangedRows,
    IReadOnlyList<ReferenceDataImportPreviewRowResponse> Preview,
    IReadOnlyList<ReferenceDataImportErrorResponse> Errors,
    bool PreviewTruncated,
    bool ErrorsTruncated,
    Guid Version);

public sealed record ReferenceDataImportCommitResponse(
    Guid ImportJobId,
    ReferenceDataImportResourceContract Resource,
    ReferenceDataImportStatusContract Status,
    int CreatedRows,
    int UpdatedRows,
    int UnchangedRows,
    DateTimeOffset CommittedAtUtc,
    Guid Version);
