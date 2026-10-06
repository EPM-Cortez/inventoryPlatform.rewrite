using Inventory.Domain.Common;

namespace Inventory.Domain.Importing;

public enum ReferenceDataImportResource
{
    People,
    Departments,
    Locations,
}

public enum ReferenceDataImportMode
{
    CreateOnly,
    UpsertByKey,
}

public enum ReferenceDataImportStatus
{
    ValidationFailed,
    ReadyToCommit,
    Committed,
}

public sealed class ReferenceDataImportJob
{
    private const int MaximumSnapshotLength = 8_000_000;
    private const int MaximumErrorsLength = 4_000_000;

    private ReferenceDataImportJob()
    {
    }

    private ReferenceDataImportJob(
        Guid id,
        ReferenceDataImportResource resource,
        int schemaVersion,
        string originalFileName,
        string fileHash,
        string snapshotHash,
        ReferenceDataImportMode mode,
        int totalRows,
        int validRows,
        int errorRows,
        int createRows,
        int updateRows,
        string validatedRowsJson,
        string errorsJson,
        DateTimeOffset createdAtUtc,
        Guid createdByUserId,
        string createdByDisplayName)
    {
        if (!Enum.IsDefined(resource))
        {
            throw new DomainRuleException(
                "invalid_import_resource",
                "The reference-data import resource is not supported.");
        }

        if (schemaVersion != 1)
        {
            throw new DomainRuleException(
                "unsupported_import_schema",
                "Only reference-data import schema version 1 is supported.");
        }

        if (!Enum.IsDefined(mode))
        {
            throw new DomainRuleException(
                "invalid_import_mode",
                "The reference-data import mode is not supported.");
        }

        ValidateUtc(createdAtUtc, nameof(CreatedAtUtc));
        ValidateActor(createdByUserId);
        ValidateCounts(totalRows, validRows, errorRows, createRows, updateRows);

        Id = id;
        Resource = resource;
        SchemaVersion = schemaVersion;
        OriginalFileName =
            TextRules.Required(originalFileName, nameof(OriginalFileName), 260);
        FileHash = ValidateHash(fileHash, nameof(FileHash));
        SnapshotHash = ValidateHash(snapshotHash, nameof(SnapshotHash));
        Mode = mode;
        Status = errorRows == 0 && totalRows > 0
            ? ReferenceDataImportStatus.ReadyToCommit
            : ReferenceDataImportStatus.ValidationFailed;
        TotalRows = totalRows;
        ValidRows = validRows;
        ErrorRows = errorRows;
        CreateRows = createRows;
        UpdateRows = updateRows;
        ValidatedRowsJson = ValidateJson(
            validatedRowsJson,
            nameof(ValidatedRowsJson),
            MaximumSnapshotLength);
        ErrorsJson = ValidateJson(errorsJson, nameof(ErrorsJson), MaximumErrorsLength);
        CreatedAtUtc = createdAtUtc;
        CreatedByUserId = createdByUserId;
        CreatedByDisplayName =
            TextRules.Required(createdByDisplayName, nameof(CreatedByDisplayName), 200);
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public ReferenceDataImportResource Resource { get; private set; }

    public int SchemaVersion { get; private set; }

    public string OriginalFileName { get; private set; } = string.Empty;

    public string FileHash { get; private set; } = string.Empty;

    public string SnapshotHash { get; private set; } = string.Empty;

    public ReferenceDataImportMode Mode { get; private set; }

    public ReferenceDataImportStatus Status { get; private set; }

    public int TotalRows { get; private set; }

    public int ValidRows { get; private set; }

    public int ErrorRows { get; private set; }

    public int CreateRows { get; private set; }

    public int UpdateRows { get; private set; }

    public string ValidatedRowsJson { get; private set; } = "[]";

    public string ErrorsJson { get; private set; } = "[]";

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public string CreatedByDisplayName { get; private set; } = string.Empty;

    public DateTimeOffset? CommittedAtUtc { get; private set; }

    public Guid? CommittedByUserId { get; private set; }

    public string? CommittedByDisplayName { get; private set; }

    public int CommittedCreateRows { get; private set; }

    public int CommittedUpdateRows { get; private set; }

    public int CommittedUnchangedRows { get; private set; }

    public Guid Version { get; private set; }

    public static ReferenceDataImportJob CreateValidation(
        ReferenceDataImportResource resource,
        int schemaVersion,
        string originalFileName,
        string fileHash,
        string snapshotHash,
        ReferenceDataImportMode mode,
        int totalRows,
        int validRows,
        int errorRows,
        int createRows,
        int updateRows,
        string validatedRowsJson,
        string errorsJson,
        DateTimeOffset createdAtUtc,
        Guid createdByUserId,
        string createdByDisplayName) =>
        new(
            Guid.NewGuid(),
            resource,
            schemaVersion,
            originalFileName,
            fileHash,
            snapshotHash,
            mode,
            totalRows,
            validRows,
            errorRows,
            createRows,
            updateRows,
            validatedRowsJson,
            errorsJson,
            createdAtUtc,
            createdByUserId,
            createdByDisplayName);

    public void PurgePreviewPayload()
    {
        ValidatedRowsJson = "[]";
        ErrorsJson = "[]";
        Version = Guid.NewGuid();
    }

    public void Commit(
        Guid expectedVersion,
        DateTimeOffset committedAtUtc,
        Guid committedByUserId,
        string committedByDisplayName,
        int committedCreateRows,
        int committedUpdateRows,
        int committedUnchangedRows)
    {
        EnsureVersion(expectedVersion);
        if (Status != ReferenceDataImportStatus.ReadyToCommit)
        {
            throw new DomainRuleException(
                "import_not_ready",
                "Only a successfully validated reference-data import can be committed.");
        }

        ValidateUtc(committedAtUtc, nameof(CommittedAtUtc));
        ValidateActor(committedByUserId);
        if (committedCreateRows < 0 ||
            committedUpdateRows < 0 ||
            committedUnchangedRows < 0 ||
            committedCreateRows + committedUpdateRows + committedUnchangedRows != ValidRows)
        {
            throw new DomainRuleException(
                "invalid_import_counts",
                "Committed import counts do not match the validated rows.");
        }

        Status = ReferenceDataImportStatus.Committed;
        CommittedAtUtc = committedAtUtc;
        CommittedByUserId = committedByUserId;
        CommittedByDisplayName =
            TextRules.Required(
                committedByDisplayName,
                nameof(CommittedByDisplayName),
                200);
        CommittedCreateRows = committedCreateRows;
        CommittedUpdateRows = committedUpdateRows;
        CommittedUnchangedRows = committedUnchangedRows;
        Version = Guid.NewGuid();
    }

    private static void ValidateCounts(
        int totalRows,
        int validRows,
        int errorRows,
        int createRows,
        int updateRows)
    {
        if (totalRows < 0 ||
            validRows < 0 ||
            errorRows < 0 ||
            createRows < 0 ||
            updateRows < 0 ||
            validRows + errorRows != totalRows ||
            createRows + updateRows != validRows)
        {
            throw new DomainRuleException(
                "invalid_import_counts",
                "Reference-data import validation counts are inconsistent.");
        }
    }

    private static string ValidateHash(string value, string fieldName)
    {
        var normalized = TextRules.Required(value, fieldName, 64).ToUpperInvariant();
        if (normalized.Length != 64 ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new DomainRuleException(
                "invalid_import_hash",
                $"{fieldName} must be a SHA-256 hash.");
        }

        return normalized;
    }

    private static string ValidateJson(string value, string fieldName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new DomainRuleException(
                "invalid_import_snapshot",
                $"{fieldName} is missing or exceeds the supported size.");
        }

        return value;
    }

    private static void ValidateActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "actor_required",
                "An import actor is required.");
        }
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || expectedVersion != Version)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The reference-data import job was changed by another user.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value, string fieldName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                $"{fieldName} must be UTC.");
        }
    }
}
