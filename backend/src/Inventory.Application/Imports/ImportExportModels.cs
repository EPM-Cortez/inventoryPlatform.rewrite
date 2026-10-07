using Inventory.Application.Assets;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Importing;
using Inventory.Domain.People;
using Inventory.Domain.Warranties;

namespace Inventory.Application.DataExchange;

public sealed class ImportExportSettings
{
    public const int SupportedSchemaVersion = 2;

    public long MaxImportBytes { get; init; } = 5 * 1024 * 1024;

    public int MaxImportRows { get; init; } = 500;

    public int MaxExportRows { get; init; } = 10_000;

    public int PreviewRows { get; init; } = 25;

    public int PreviewErrors { get; init; } = 100;

    public string LegacyDefaultCurrency { get; init; } = "GBP";

    public string GeneratedAssetTagPrefix { get; init; } = "AUTO";

    public void Validate()
    {
        if (MaxImportBytes is < 1024 or > 50 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "ImportExport:MaxImportBytes must be between 1 KB and 50 MB.");
        }

        if (MaxImportRows is < 1 or > 10_000)
        {
            throw new InvalidOperationException(
                "ImportExport:MaxImportRows must be between 1 and 10000.");
        }

        if (MaxExportRows is < 1 or > 10_000)
        {
            throw new InvalidOperationException(
                "ImportExport:MaxExportRows must be between 1 and 10000.");
        }

        if (PreviewRows is < 1 or > 200 || PreviewErrors is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "Import/export preview limits are outside the supported range.");
        }

        if (LegacyDefaultCurrency.Length != 3 ||
            LegacyDefaultCurrency.Any(character => !char.IsAsciiLetter(character)))
        {
            throw new InvalidOperationException(
                "ImportExport:LegacyDefaultCurrency must be a three-letter ISO code.");
        }

        var generatedAssetTagPrefix = GeneratedAssetTagPrefix?.Trim();
        if (string.IsNullOrWhiteSpace(generatedAssetTagPrefix) ||
            generatedAssetTagPrefix.Length is < 1 or > 50 ||
            !char.IsAsciiLetterOrDigit(generatedAssetTagPrefix[0]) ||
            !char.IsAsciiLetterOrDigit(generatedAssetTagPrefix[^1]) ||
            generatedAssetTagPrefix.Any(
                character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new InvalidOperationException(
                "ImportExport:GeneratedAssetTagPrefix must contain 1 to 50 ASCII " +
                "letters, digits, or internal hyphens.");
        }
    }
}

public sealed record AssetImportFile(
    string FileName,
    string ContentType,
    long Length,
    Stream Content);

public sealed record AssetImportError(
    int RowNumber,
    string Column,
    string Code,
    string Message,
    string? Value);

public enum AssetImportAction
{
    Create,
    Update,
}

public enum AssetImportAssignmentResolution
{
    None,
    ExactPerson,
    FuzzyPerson,
    Department,
    Unassigned,
}

public enum AssetImportSerialResolution
{
    Missing,
    Verified,
    ReportedDuplicate,
    IgnoredPlaceholder,
}

public sealed record AssetImportPreviewRow(
    int RowNumber,
    AssetImportAction Action,
    string AssetTag,
    bool AssetTagGenerated,
    string Category,
    string ResolvedCategoryCode,
    bool CategoryWillBeCreated,
    AssetStatus Status,
    AssetCondition Condition,
    string? LocationCode,
    string? ResolvedLocationCode,
    bool LocationWillBeCreated,
    string? AssignmentSource,
    AssetImportAssignmentResolution AssignmentResolution,
    string? AssignedTo,
    DateOnly? PurchaseDate,
    DateOnly? WarrantyStartDate,
    DateOnly? WarrantyEndDate,
    IReadOnlyList<string> InferredDateFields,
    string? SourceSerialNumber = null,
    AssetImportSerialResolution SerialResolution = AssetImportSerialResolution.Missing);

public sealed record AssetImportJobView(
    Guid Id,
    int SchemaVersion,
    string OriginalFileName,
    string FileHash,
    AssetImportMode Mode,
    AssetImportStatus Status,
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
    IReadOnlyList<AssetImportPreviewRow> Preview,
    IReadOnlyList<AssetImportError> Errors,
    bool PreviewTruncated,
    bool ErrorsTruncated,
    Guid Version,
    int ReportedDuplicateSerialRows = 0);

public sealed record AssetImportCommitResult(
    Guid ImportJobId,
    AssetImportStatus Status,
    int CreatedRows,
    int UpdatedRows,
    int UnchangedRows,
    int CreatedCategoryCount,
    int CreatedLocationCount,
    DateTimeOffset CommittedAtUtc,
    Guid Version);

public sealed record AssetExportFilter(
    string? Query,
    string? AssetTag,
    string? SerialNumber,
    Guid? CategoryId,
    AssetStatus? Status,
    AssetCondition? Condition,
    Guid? LocationId,
    Guid? DepartmentId,
    Guid? AssignedPersonId,
    bool? Assigned,
    WarrantyState? WarrantyState,
    DateOnly? WarrantyEndsBefore,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo,
    string? LegacyId,
    string Sort);

public sealed record AssetCsvFile(
    byte[] Content,
    string FileName,
    int RowCount);

public sealed record ImportLookupRequest(
    IReadOnlyCollection<string> CategoryKeys,
    IReadOnlyCollection<string> LocationKeys,
    IReadOnlyCollection<string> DepartmentCodes,
    IReadOnlyCollection<string> PersonExternalIds,
    IReadOnlyCollection<string> AssetTags,
    IReadOnlyCollection<string> SerialNumbers);

public sealed record ImportReferenceLookup(
    Guid Id,
    string Code,
    string Name,
    bool IsActive,
    Guid Version);

public sealed record ImportPersonLookup(
    Guid Id,
    string ExternalId,
    string DisplayName,
    bool IsActive,
    Guid Version);

public sealed record ImportAssignmentLookup(
    Guid Id,
    Guid AssetId,
    Guid? PersonId,
    Guid? DepartmentId,
    Guid Version);

public sealed record ImportWarrantyLookup(
    Guid Id,
    Guid AssetId,
    string Provider,
    string CoverageType,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Reference,
    Guid Version);

public sealed record ImportAssetLookup(
    Guid Id,
    string AssetTagNormalized,
    string? SerialNumber,
    string? ReportedSerialNumber,
    string? SerialNumberNormalized,
    AssetStatus Status,
    Guid Version,
    ImportAssignmentLookup? Assignment,
    ImportWarrantyLookup? Warranty);

public sealed record ImportLookupSnapshot(
    IReadOnlyList<ImportReferenceLookup> Categories,
    IReadOnlyList<ImportReferenceLookup> Locations,
    IReadOnlyList<ImportReferenceLookup> Departments,
    IReadOnlyList<ImportPersonLookup> People,
    IReadOnlyList<ImportAssetLookup> Assets);

public sealed record ImportCommitLookupRequest(
    IReadOnlyCollection<Guid> AssetIds,
    IReadOnlyCollection<string> AssetTags,
    IReadOnlyCollection<string> SerialNumbers,
    IReadOnlyCollection<Guid> CategoryIds,
    IReadOnlyCollection<string> CategoryKeys,
    IReadOnlyCollection<Guid> LocationIds,
    IReadOnlyCollection<string> LocationKeys,
    IReadOnlyCollection<Guid> PersonIds,
    IReadOnlyCollection<Guid> DepartmentIds);

public sealed record ImportCommitState(
    IReadOnlyList<Asset> Assets,
    IReadOnlyList<AssetCategory> Categories,
    IReadOnlyList<Inventory.Domain.Locations.Location> Locations,
    IReadOnlyList<Person> People,
    IReadOnlyList<Department> Departments,
    IReadOnlyList<AssetAssignment> Assignments,
    IReadOnlyList<WarrantyCoverage> Warranties);

public sealed record AssetExportRow(
    string AssetTag,
    string? DeviceName,
    string CategoryCode,
    string? Description,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? LanMacAddress,
    Guid? BiosGuid,
    string? Ownership,
    AssetStatus Status,
    AssetCondition Condition,
    string? LocationCode,
    string? DepartmentCode,
    string? AssignedPersonExternalId,
    DateOnly? PurchaseDate,
    decimal? PurchaseCost,
    string? Currency,
    string? Supplier,
    DateOnly? WarrantyStartDate,
    DateOnly? WarrantyEndDate,
    string? WarrantyProvider,
    string? WarrantyReference,
    string? OperatingSystem,
    string? OperatingSystemVersion,
    bool? Windows11Compliant,
    string? LegacyId,
    string? Notes,
    string? ReportedSerialNumber = null);

internal sealed record ValidatedAssetImportRow(
    int RowNumber,
    AssetImportAction Action,
    bool PreserveVersion2Fields,
    bool AssetTagGenerated,
    Guid? AssetId,
    Guid? ExpectedAssetVersion,
    Guid? ExpectedAssignmentId,
    Guid? ExpectedAssignmentVersion,
    Guid? ExpectedWarrantyId,
    Guid? ExpectedWarrantyVersion,
    Guid? CategoryId,
    Guid? ExpectedCategoryVersion,
    Guid? LocationId,
    Guid? ExpectedLocationVersion,
    Guid? PersonId,
    Guid? ExpectedPersonVersion,
    Guid? DepartmentId,
    Guid? ExpectedDepartmentVersion,
    string AssetTag,
    string AssetTagNormalized,
    string? DeviceName,
    string Category,
    string? Description,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? SerialNumberNormalized,
    string? LanMacAddress,
    Guid? BiosGuid,
    string? Ownership,
    AssetStatus Status,
    AssetCondition Condition,
    string? LocationCode,
    string? DepartmentCode,
    string? AssignedPersonExternalId,
    string? AssignedToDisplayName,
    DateOnly? PurchaseDate,
    decimal? PurchaseCost,
    string? Currency,
    string? Supplier,
    DateOnly? WarrantyStartDate,
    DateOnly? WarrantyEndDate,
    string? WarrantyProvider,
    string? WarrantyReference,
    string? OperatingSystem,
    string? OperatingSystemVersion,
    bool? Windows11Compliant,
    string? LegacyId,
    string? Notes,
    string? ResolvedCategoryCode = null,
    string? ResolvedCategoryName = null,
    bool CategoryWillBeCreated = false,
    string? ResolvedLocationCode = null,
    string? ResolvedLocationName = null,
    bool LocationWillBeCreated = false,
    IReadOnlyList<string>? InferredDateFields = null,
    AssetImportAssignmentResolution AssignmentResolution =
        AssetImportAssignmentResolution.None,
    string? SourceSerialNumber = null,
    AssetImportSerialResolution SerialResolution = AssetImportSerialResolution.Missing);
