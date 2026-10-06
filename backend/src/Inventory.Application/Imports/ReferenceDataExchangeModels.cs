using Inventory.Domain.Importing;
using Inventory.Domain.People;

namespace Inventory.Application.DataExchange;

public static class ReferenceDataCsvSchemas
{
    public const int SupportedSchemaVersion = 1;

    public static IReadOnlyList<string> PeopleHeaders { get; } =
    [
        "schemaVersion",
        "externalId",
        "displayName",
        "personType",
        "email",
        "isActive",
    ];

    public static IReadOnlyList<string> StaffCompatibilityHeaders { get; } =
    [
        "displayName",
        "personType",
        "email",
    ];

    public static IReadOnlyList<string> DepartmentHeaders { get; } =
    [
        "schemaVersion",
        "code",
        "name",
        "isActive",
    ];

    public static IReadOnlyList<string> DepartmentCompatibilityHeaders { get; } =
    [
        "name",
        "code",
    ];

    public static IReadOnlyList<string> LocationHeaders { get; } =
    [
        "schemaVersion",
        "code",
        "name",
        "type",
        "parentCode",
        "isActive",
    ];

    public static IReadOnlyList<string> LocationCompatibilityHeaders { get; } =
    [
        "Name",
        "Type",
        "Code",
    ];
}

public sealed record ReferenceDataImportFile(
    string FileName,
    string ContentType,
    long Length,
    Stream Content);

public sealed record ReferenceDataImportError(
    int RowNumber,
    string Column,
    string Code,
    string Message,
    string? Value);

public enum ReferenceDataImportAction
{
    Create,
    Update,
}

public sealed record ReferenceDataImportPreviewRow(
    int RowNumber,
    ReferenceDataImportAction Action,
    string Key,
    bool KeyGenerated,
    string Name,
    string? Type,
    string? ParentKey,
    bool IsActive);

public sealed record ReferenceDataImportJobView(
    Guid Id,
    ReferenceDataImportResource Resource,
    int SchemaVersion,
    string OriginalFileName,
    string FileHash,
    ReferenceDataImportMode Mode,
    ReferenceDataImportStatus Status,
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
    IReadOnlyList<ReferenceDataImportPreviewRow> Preview,
    IReadOnlyList<ReferenceDataImportError> Errors,
    bool PreviewTruncated,
    bool ErrorsTruncated,
    Guid Version);

public sealed record ReferenceDataImportCommitResult(
    Guid ImportJobId,
    ReferenceDataImportResource Resource,
    ReferenceDataImportStatus Status,
    int CreatedRows,
    int UpdatedRows,
    int UnchangedRows,
    DateTimeOffset CommittedAtUtc,
    Guid Version);

public sealed record ReferenceDataCsvFile(
    byte[] Content,
    string FileName,
    int RowCount);

internal interface IValidatedReferenceDataImportRow
{
    int RowNumber { get; }

    ReferenceDataImportAction Action { get; }

    bool KeyGenerated { get; }

    string Key { get; }

    string Name { get; }

    string? PreviewType { get; }

    string? ParentKey { get; }

    bool IsActive { get; }
}

internal sealed record ValidatedPersonImportRow(
    int RowNumber,
    ReferenceDataImportAction Action,
    bool KeyGenerated,
    Guid? PersonId,
    Guid? ExpectedVersion,
    string ExternalId,
    string ExternalIdNormalized,
    string DisplayName,
    PersonType PersonType,
    string? Email,
    string? EmailNormalized,
    bool IsActive) : IValidatedReferenceDataImportRow
{
    public string Key => ExternalId;

    public string Name => DisplayName;

    public string? PreviewType => PersonType.ToString();

    public string? ParentKey => null;
}

internal sealed record ValidatedDepartmentImportRow(
    int RowNumber,
    ReferenceDataImportAction Action,
    bool KeyGenerated,
    Guid? DepartmentId,
    Guid? ExpectedVersion,
    string Code,
    string CodeNormalized,
    string DepartmentName,
    bool IsActive) : IValidatedReferenceDataImportRow
{
    public string Key => Code;

    public string Name => DepartmentName;

    public string? PreviewType => null;

    public string? ParentKey => null;
}

internal sealed record ValidatedLocationImportRow(
    int RowNumber,
    ReferenceDataImportAction Action,
    bool KeyGenerated,
    Guid? LocationId,
    Guid? ExpectedVersion,
    string Code,
    string CodeNormalized,
    string LocationName,
    string? LocationType,
    string? ParentCode,
    string? ParentCodeNormalized,
    bool IsActive) : IValidatedReferenceDataImportRow
{
    public string Key => Code;

    public string Name => LocationName;

    public string? PreviewType => LocationType;

    public string? ParentKey => ParentCode;
}
