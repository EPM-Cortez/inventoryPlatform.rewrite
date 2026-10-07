using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Auditing;
using Inventory.Domain.Common;
using Inventory.Domain.Importing;

namespace Inventory.Application.DataExchange;

public sealed partial class ReferenceDataExchangeService
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = CreateJsonOptions();

    private static readonly HashSet<string> AllowedContentTypes = new(
        [
            "text/csv",
            "application/csv",
            "application/vnd.ms-excel",
            "application/octet-stream",
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly IReferenceDataExchangeRepository _repository;
    private readonly ILocationGraphMutationRunner _locationGraph;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;
    private readonly ImportExportSettings _settings;
    private readonly IImportPreviewBudget _previewBudget;

    public ReferenceDataExchangeService(
        IReferenceDataExchangeRepository repository,
        ILocationGraphMutationRunner locationGraph,
        IClock clock,
        ICurrentUserContext currentUser,
        ImportExportSettings settings,
        IImportPreviewBudget previewBudget)
    {
        _repository = repository;
        _locationGraph = locationGraph;
        _clock = clock;
        _currentUser = currentUser;
        _settings = settings;
        _previewBudget = previewBudget;
    }

    public Task<ReferenceDataImportJobView> ValidatePeopleImportAsync(
        ReferenceDataImportFile file,
        ReferenceDataImportMode mode,
        CancellationToken cancellationToken) =>
        ValidatePeopleAsync(file, mode, cancellationToken);

    public Task<ReferenceDataImportJobView> ValidateDepartmentImportAsync(
        ReferenceDataImportFile file,
        ReferenceDataImportMode mode,
        CancellationToken cancellationToken) =>
        ValidateDepartmentsAsync(file, mode, cancellationToken);

    public Task<ReferenceDataImportJobView> ValidateLocationImportAsync(
        ReferenceDataImportFile file,
        ReferenceDataImportMode mode,
        CancellationToken cancellationToken) =>
        ValidateLocationsAsync(file, mode, cancellationToken);

    public async Task<ReferenceDataImportJobView> GetImportJobAsync(
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        var job = await _repository.GetImportJobAsync(
            importJobId,
            forUpdate: false,
            cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Reference-data import job",
                importJobId);
        if (job.Status != ReferenceDataImportStatus.Committed)
        {
            await _previewBudget.EnsureAvailableAsync(
                job.Id, ImportPreviewPayloadKind.ReferenceData, cancellationToken);
        }
        return ToView(job);
    }

    public async Task<ReferenceDataCsvFile> GetImportErrorsCsvAsync(
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        var job = await _repository.GetImportJobAsync(
            importJobId,
            forUpdate: false,
            cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Reference-data import job",
                importJobId);
        if (job.Status != ReferenceDataImportStatus.Committed)
        {
            await _previewBudget.EnsureAvailableAsync(
                job.Id, ImportPreviewPayloadKind.ReferenceData, cancellationToken);
        }
        var errors = DeserializeErrors(job.ErrorsJson);
        var rows = errors.Select(
            error => (IReadOnlyList<string?>)
            [
                error.RowNumber.ToString(CultureInfo.InvariantCulture),
                error.Column,
                error.Code,
                error.Message,
                error.Value,
            ]);
        var content = AssetCsvCodec.Write(
            ["row", "column", "code", "message", "value"],
            rows);
        return new ReferenceDataCsvFile(
            content,
            $"{ResourceSlug(job.Resource)}-import-{job.Id:N}-errors.csv",
            errors.Length);
    }

    public async Task<ReferenceDataImportCommitResult> CommitImportAsync(
        Guid importJobId,
        Guid version,
        CancellationToken cancellationToken)
    {
        var job = await _repository.GetImportJobAsync(
            importJobId,
            forUpdate: true,
            cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Reference-data import job",
                importJobId);
        var actor = _currentUser.RequireActor();

        if (job.Status == ReferenceDataImportStatus.Committed)
        {
            throw await RecordCommitRejectionAsync(
                job, actor, "import_not_ready",
                "The import job has already been committed.", cancellationToken);
        }

        await _previewBudget.EnsureAvailableAsync(
            job.Id, ImportPreviewPayloadKind.ReferenceData, cancellationToken);

        if (!await _previewBudget.IsWithinRetentionAsync(
                job.Id, ImportPreviewPayloadKind.ReferenceData, cancellationToken))
        {
            throw new ResourceConflictException(
                "import_preview_expired",
                "This import preview has expired. Upload and validate the file again.");
        }

        if (job.Status != ReferenceDataImportStatus.ReadyToCommit)
        {
            throw await RecordCommitRejectionAsync(
                job,
                actor,
                "import_not_ready",
                job.Status == ReferenceDataImportStatus.Committed
                    ? "The import job has already been committed."
                    : "The import job contains validation errors.",
                cancellationToken);
        }

        if (version == Guid.Empty || version != job.Version)
        {
            throw await RecordCommitRejectionAsync(
                job,
                actor,
                "concurrency_conflict",
                "The import job was changed by another user.",
                cancellationToken);
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(job.SnapshotHash),
                Convert.FromHexString(Hash(Encoding.UTF8.GetBytes(job.ValidatedRowsJson)))))
        {
            throw await RecordCommitRejectionAsync(
                job,
                actor,
                "import_snapshot_invalid",
                "The validated import snapshot no longer matches its recorded hash.",
                cancellationToken);
        }

        if (job.Resource == ReferenceDataImportResource.Locations)
        {
            try
            {
                return await _locationGraph.RunAsync(
                    async token =>
                    {
                        var locationCounts = await CommitLocationsAsync(job, token);
                        return await ApplyValidatedCommitAsync(
                            job,
                            version,
                            actor,
                            locationCounts,
                            token);
                    },
                    cancellationToken);
            }
            catch (PersistenceConflictException exception)
            {
                throw await RecordCommitRejectionAsync(
                    job,
                    actor,
                    exception.Code,
                    exception.Message,
                    cancellationToken,
                    latePersistenceConflict: job.Status == ReferenceDataImportStatus.Committed);
            }
            catch (ResourceConflictException exception)
            {
                throw await RecordCommitRejectionAsync(
                    job,
                    actor,
                    exception.Code,
                    exception.Message,
                    cancellationToken,
                    latePersistenceConflict: job.Status == ReferenceDataImportStatus.Committed);
            }
        }

        CommitCounts counts;
        try
        {
            counts = job.Resource switch
            {
                ReferenceDataImportResource.People =>
                    await CommitPeopleAsync(job, cancellationToken),
                ReferenceDataImportResource.Departments =>
                    await CommitDepartmentsAsync(job, cancellationToken),
                _ => throw new InvalidOperationException(
                    "The reference-data import resource is not supported."),
            };
        }
        catch (ResourceConflictException exception)
        {
            throw await RecordCommitRejectionAsync(
                job,
                actor,
                exception.Code,
                exception.Message,
                cancellationToken);
        }

        try
        {
            return await ApplyValidatedCommitAsync(
                job,
                version,
                actor,
                counts,
                cancellationToken);
        }
        catch (ResourceConflictException exception)
        {
            throw await RecordCommitRejectionAsync(
                job,
                actor,
                exception.Code,
                exception.Message,
                cancellationToken,
                latePersistenceConflict: job.Status == ReferenceDataImportStatus.Committed);
        }
    }

    private async Task<ReferenceDataImportCommitResult> ApplyValidatedCommitAsync(
        ReferenceDataImportJob job,
        Guid version,
        CurrentActor actor,
        CommitCounts counts,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        try
        {
            job.Commit(
                version,
                now,
                actor.UserId,
                actor.DisplayName,
                counts.Created,
                counts.Updated,
                counts.Unchanged);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _repository.AddAuditLog(
            CreateAudit(
                actor,
                "reference_data.import.committed",
                "reference_data_import_job",
                job.Id.ToString(),
                "succeeded",
                new
                {
                    job.Resource,
                    job.FileHash,
                    job.Mode,
                    createdRows = counts.Created,
                    updatedRows = counts.Updated,
                    unchangedRows = counts.Unchanged,
                }));

        await _previewBudget.ReleaseCommittedAsync(
            job.Id, ImportPreviewPayloadKind.ReferenceData, cancellationToken);

        await SaveChangesAsync(cancellationToken);

        return new ReferenceDataImportCommitResult(
            job.Id,
            job.Resource,
            job.Status,
            counts.Created,
            counts.Updated,
            counts.Unchanged,
            now,
            job.Version);
    }

    public async Task<ReferenceDataCsvFile> ExportPeopleAsync(
        CancellationToken cancellationToken)
    {
        var people = await _repository.ExportPeopleAsync(
            _settings.MaxExportRows + 1,
            cancellationToken);
        EnsureExportWithinLimit(
            ReferenceDataImportResource.People,
            people.Count);

        var content = AssetCsvCodec.Write(
            ReferenceDataCsvSchemas.PeopleHeaders,
            people.Select(
                person => (IReadOnlyList<string?>)
                [
                    ReferenceDataCsvSchemas.SupportedSchemaVersion.ToString(
                        CultureInfo.InvariantCulture),
                    person.ExternalId,
                    person.DisplayName,
                    person.Type.ToString(),
                    person.Email,
                    person.IsActive.ToString().ToLowerInvariant(),
                ]));
        await AuditExportAsync(
            ReferenceDataImportResource.People,
            people.Count,
            cancellationToken);
        return new ReferenceDataCsvFile(
            content,
            $"people-{_clock.UtcNow:yyyyMMdd-HHmmss}.csv",
            people.Count);
    }

    public async Task<ReferenceDataCsvFile> ExportDepartmentsAsync(
        CancellationToken cancellationToken)
    {
        var departments = await _repository.ExportDepartmentsAsync(
            _settings.MaxExportRows + 1,
            cancellationToken);
        EnsureExportWithinLimit(
            ReferenceDataImportResource.Departments,
            departments.Count);

        var content = AssetCsvCodec.Write(
            ReferenceDataCsvSchemas.DepartmentHeaders,
            departments.Select(
                department => (IReadOnlyList<string?>)
                [
                    ReferenceDataCsvSchemas.SupportedSchemaVersion.ToString(
                        CultureInfo.InvariantCulture),
                    department.Code,
                    department.Name,
                    department.IsActive.ToString().ToLowerInvariant(),
                ]));
        await AuditExportAsync(
            ReferenceDataImportResource.Departments,
            departments.Count,
            cancellationToken);
        return new ReferenceDataCsvFile(
            content,
            $"departments-{_clock.UtcNow:yyyyMMdd-HHmmss}.csv",
            departments.Count);
    }

    public async Task<ReferenceDataCsvFile> ExportLocationsAsync(
        CancellationToken cancellationToken)
    {
        var locations = await _repository.ExportLocationsAsync(
            _settings.MaxExportRows + 1,
            cancellationToken);
        EnsureExportWithinLimit(
            ReferenceDataImportResource.Locations,
            locations.Count);
        var locationsById = locations.ToDictionary(location => location.Id);

        var content = AssetCsvCodec.Write(
            ReferenceDataCsvSchemas.LocationHeaders,
            locations.Select(
                location => (IReadOnlyList<string?>)
                [
                    ReferenceDataCsvSchemas.SupportedSchemaVersion.ToString(
                        CultureInfo.InvariantCulture),
                    location.Code,
                    location.Name,
                    location.Type,
                    location.ParentLocationId.HasValue &&
                    locationsById.TryGetValue(location.ParentLocationId.Value, out var parent)
                        ? parent.Code
                        : null,
                    location.IsActive.ToString().ToLowerInvariant(),
                ]));
        await AuditExportAsync(
            ReferenceDataImportResource.Locations,
            locations.Count,
            cancellationToken);
        return new ReferenceDataCsvFile(
            content,
            $"locations-{_clock.UtcNow:yyyyMMdd-HHmmss}.csv",
            locations.Count);
    }

    private async Task<PreparedImportFile> PrepareFileAsync(
        ReferenceDataImportFile file,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var fileName = ValidateFileMetadata(file);
        var content = await ReadBoundedAsync(file.Content, cancellationToken);
        if (content.Length == 0)
        {
            throw InvalidFile("The CSV file is empty.");
        }

        var fileHash = Hash(content);
        try
        {
            var document = AssetCsvCodec.Parse(AssetCsvCodec.DecodeStrictUtf8(content));
            return new PreparedImportFile(fileName, fileHash, document, null);
        }
        catch (CsvFormatException exception)
        {
            return new PreparedImportFile(
                fileName,
                fileHash,
                null,
                FileError("invalid_csv", exception.Message));
        }
    }

    private ParsedReferenceCsv ParseRows(
        PreparedImportFile prepared,
        IReadOnlyList<string> canonicalHeaders,
        IReadOnlyList<string> compatibilityHeaders)
    {
        if (prepared.ParseError is not null)
        {
            return new ParsedReferenceCsv(
                CsvInputFormat.Canonical,
                [],
                prepared.ParseError);
        }

        var records = prepared.Document!.Records;
        if (records.Count == 0)
        {
            return new ParsedReferenceCsv(
                CsvInputFormat.Canonical,
                [],
                FileError("missing_header", "The CSV header row is missing."));
        }

        CsvInputFormat format;
        if (HeaderMatches(records[0], canonicalHeaders))
        {
            format = CsvInputFormat.Canonical;
        }
        else if (HeaderMatches(records[0], compatibilityHeaders))
        {
            format = CsvInputFormat.Compatibility;
        }
        else
        {
            return new ParsedReferenceCsv(
                CsvInputFormat.Canonical,
                [],
                FileError(
                    "invalid_header",
                    "The CSV header does not match the canonical or supported " +
                    "compatibility format."));
        }

        var dataRecords = records
            .Skip(1)
            .Select((record, index) => new CsvSourceRecord(index + 2, record))
            .Where(source => source.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToArray();
        if (dataRecords.Length == 0)
        {
            return new ParsedReferenceCsv(
                format,
                [],
                FileError("no_data_rows", "The CSV file contains no data rows."));
        }

        if (dataRecords.Length > _settings.MaxImportRows)
        {
            throw new RequestValidationException(
                "import_row_limit_exceeded",
                $"The CSV contains {dataRecords.Length} rows; the limit is " +
                $"{_settings.MaxImportRows}.",
                new Dictionary<string, string[]>
                {
                    ["file"] =
                    [
                        $"The CSV cannot contain more than {_settings.MaxImportRows} rows.",
                    ],
                });
        }

        return new ParsedReferenceCsv(format, dataRecords, null);
    }

    private async Task<ReferenceDataImportJobView> PersistValidationAsync<T>(
        ReferenceDataImportResource resource,
        string fileName,
        string fileHash,
        ReferenceDataImportMode mode,
        IReadOnlyList<T> rows,
        List<ReferenceDataImportError> errors,
        int totalRows,
        CurrentActor actor,
        CancellationToken cancellationToken)
        where T : IValidatedReferenceDataImportRow
    {
        var errorRowCount = errors
            .Where(error => error.RowNumber >= 2)
            .Select(error => error.RowNumber)
            .Distinct()
            .Count();
        if (rows.Count + errorRowCount != totalRows)
        {
            throw new InvalidOperationException(
                "Reference-data import validation did not account for every data row.");
        }

        var rowsJson = JsonSerializer.Serialize(rows, SnapshotJsonOptions);
        var errorsJson = JsonSerializer.Serialize(errors, SnapshotJsonOptions);
        var now = _clock.UtcNow;
        ReferenceDataImportJob job;
        try
        {
            job = ReferenceDataImportJob.CreateValidation(
                resource,
                ReferenceDataCsvSchemas.SupportedSchemaVersion,
                fileName,
                fileHash,
                Hash(Encoding.UTF8.GetBytes(rowsJson)),
                mode,
                totalRows,
                rows.Count,
                errorRowCount,
                rows.Count(row => row.Action == ReferenceDataImportAction.Create),
                rows.Count(row => row.Action == ReferenceDataImportAction.Update),
                rowsJson,
                errorsJson,
                now,
                actor.UserId,
                actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        var payloadBytes = checked(
            Encoding.UTF8.GetByteCount(rowsJson) +
            Encoding.UTF8.GetByteCount(errorsJson));
        return await _previewBudget.AdmitAsync(
            job.Id,
            ImportPreviewPayloadKind.ReferenceData,
            actor.UserId,
            payloadBytes,
            now,
            async token =>
            {
                _repository.AddImportJob(job);
                _repository.AddAuditLog(
            CreateAudit(
                actor,
                "reference_data.import.validated",
                "reference_data_import_job",
                job.Id.ToString(),
                errors.Count == 0 ? "succeeded" : "validation_failed",
                new
                {
                    resource,
                    fileName,
                    fileHash,
                    schemaVersion = ReferenceDataCsvSchemas.SupportedSchemaVersion,
                    mode,
                    totalRows,
                    validRows = rows.Count,
                    errorRows = errorRowCount,
                    errorCount = errors.Count,
                    generatedKeyRows = rows.Count(row => row.KeyGenerated),
                }));
                await SaveChangesAsync(token);
                return ToView(job);
            },
            cancellationToken);
    }

    private ReferenceDataImportJobView ToView(ReferenceDataImportJob job)
    {
        var rows = DeserializeRows(job);
        var errors = DeserializeErrors(job.ErrorsJson);
        return new ReferenceDataImportJobView(
            job.Id,
            job.Resource,
            job.SchemaVersion,
            job.OriginalFileName,
            job.FileHash,
            job.Mode,
            job.Status,
            job.TotalRows,
            job.ValidRows,
            job.ErrorRows,
            job.CreateRows,
            job.UpdateRows,
            rows.Count(row => row.KeyGenerated),
            job.CreatedAtUtc,
            job.CreatedByUserId,
            job.CreatedByDisplayName,
            job.CommittedAtUtc,
            job.CommittedByUserId,
            job.CommittedByDisplayName,
            job.CommittedCreateRows,
            job.CommittedUpdateRows,
            job.CommittedUnchangedRows,
            rows
                .Take(_settings.PreviewRows)
                .Select(
                    row => new ReferenceDataImportPreviewRow(
                        row.RowNumber,
                        row.Action,
                        row.Key,
                        row.KeyGenerated,
                        row.Name,
                        row.PreviewType,
                        row.ParentKey,
                        row.IsActive))
                .ToArray(),
            errors.Take(_settings.PreviewErrors).ToArray(),
            rows.Count > _settings.PreviewRows,
            errors.Length > _settings.PreviewErrors,
            job.Version);
    }

    private static IReadOnlyList<IValidatedReferenceDataImportRow> DeserializeRows(
        ReferenceDataImportJob job) =>
        job.Resource switch
        {
            ReferenceDataImportResource.People =>
                Deserialize<ValidatedPersonImportRow>(job.ValidatedRowsJson),
            ReferenceDataImportResource.Departments =>
                Deserialize<ValidatedDepartmentImportRow>(job.ValidatedRowsJson),
            ReferenceDataImportResource.Locations =>
                Deserialize<ValidatedLocationImportRow>(job.ValidatedRowsJson),
            _ => throw new InvalidOperationException(
                "The reference-data import resource is not supported."),
        };

    private static T[] Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T[]>(json, SnapshotJsonOptions)
                ?? throw new InvalidOperationException(
                    "The reference-data import snapshot could not be read.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The reference-data import snapshot is invalid.",
                exception);
        }
    }

    private static ReferenceDataImportError[] DeserializeErrors(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ReferenceDataImportError[]>(
                    json,
                    SnapshotJsonOptions)
                ?? throw new InvalidOperationException(
                    "The reference-data import errors could not be read.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The reference-data import errors are invalid.",
                exception);
        }
    }

    private string ValidateFileMetadata(ReferenceDataImportFile file)
    {
        var fileName = Path.GetFileName(file.FileName?.Trim());
        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
            fileName.Length > 260)
        {
            throw InvalidFile("The upload must have a valid .csv file name.");
        }

        if (!AllowedContentTypes.Contains(file.ContentType?.Trim() ?? string.Empty))
        {
            throw InvalidFile("The upload content type is not a supported CSV type.");
        }

        if (file.Length <= 0 || file.Length > _settings.MaxImportBytes)
        {
            throw InvalidFile(
                $"The CSV file must be between 1 byte and {_settings.MaxImportBytes} bytes.");
        }

        return fileName;
    }

    private async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        await using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > _settings.MaxImportBytes)
            {
                throw InvalidFile(
                    $"The CSV file exceeds the {_settings.MaxImportBytes}-byte limit.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private void EnsureExportWithinLimit(
        ReferenceDataImportResource resource,
        int rowCount)
    {
        if (rowCount <= _settings.MaxExportRows)
        {
            return;
        }

        throw new RequestValidationException(
            "export_row_limit_exceeded",
            $"The {ResourceSlug(resource)} export exceeds the " +
            $"{_settings.MaxExportRows}-row limit.");
    }

    private async Task AuditExportAsync(
        ReferenceDataImportResource resource,
        int rowCount,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        _repository.AddAuditLog(
            CreateAudit(
                actor,
                "reference_data.exported",
                ResourceSlug(resource),
                null,
                "succeeded",
                new
                {
                    resource,
                    schemaVersion = ReferenceDataCsvSchemas.SupportedSchemaVersion,
                    rowCount,
                }));
        await SaveChangesAsync(cancellationToken);
    }

    private AuditLog CreateAudit(
        CurrentActor actor,
        string action,
        string targetType,
        string? targetId,
        string outcome,
        object details) =>
        AuditLog.Create(
            actor.UserId,
            actor.DisplayName,
            action,
            targetType,
            targetId,
            _clock.UtcNow,
            outcome,
            JsonSerializer.Serialize(details, SnapshotJsonOptions),
            actor.IpAddress,
            actor.Client,
            actor.CorrelationId);

    private async Task<ResourceConflictException> RecordCommitRejectionAsync(
        ReferenceDataImportJob job,
        CurrentActor actor,
        string code,
        string message,
        CancellationToken cancellationToken,
        bool latePersistenceConflict = false)
    {
        object details = latePersistenceConflict
            ? new
            {
                code,
                job.Resource,
                job.FileHash,
                job.Mode,
                status = ReferenceDataImportStatus.ReadyToCommit,
                reason = "late_persistence_conflict",
            }
            : new
            {
                code,
                job.Resource,
                job.FileHash,
                job.Mode,
                job.Status,
            };
        var audit = CreateAudit(
                actor,
                "reference_data.import.commit_rejected",
                "reference_data_import_job",
                job.Id.ToString(),
                "rejected",
                details);
        try
        {
            // A rejected import may have mutated several tracked rows before a
            // later validation or persistence conflict. Clear them before
            // recording the rejection in its own committed unit of work.
            await _repository.SaveRejectedCommitAuditAfterFailureAsync(
                audit,
                cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new InvalidOperationException(
                "The import commit failed and its rejection audit could not be persisted.",
                exception);
        }

        return new ResourceConflictException(code, message);
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new ResourceConflictException(exception.Code, exception.Message, exception);
        }
    }

    private static bool HeaderMatches(
        IReadOnlyList<string> actual,
        IReadOnlyList<string> expected) =>
        actual.Count == expected.Count &&
        actual.Zip(expected).All(pair => pair.First == pair.Second);

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content));

    private static ReferenceDataImportError FileError(string code, string message) =>
        new(0, "file", code, message, null);

    private static RequestValidationException InvalidFile(string message) =>
        new(
            "invalid_import_file",
            message,
            new Dictionary<string, string[]>
            {
                ["file"] = [message],
            });

    private static string ResourceSlug(ReferenceDataImportResource resource) =>
        resource switch
        {
            ReferenceDataImportResource.People => "people",
            ReferenceDataImportResource.Departments => "departments",
            ReferenceDataImportResource.Locations => "locations",
            _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null),
        };

    private static InventoryException ToApplicationException(DomainRuleException exception) =>
        exception.Code == "concurrency_conflict"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));
        return options;
    }

    private enum CsvInputFormat
    {
        Canonical,
        Compatibility,
    }

    private sealed record PreparedImportFile(
        string FileName,
        string FileHash,
        ParsedCsvDocument? Document,
        ReferenceDataImportError? ParseError);

    private sealed record ParsedReferenceCsv(
        CsvInputFormat Format,
        IReadOnlyList<CsvSourceRecord> Records,
        ReferenceDataImportError? FileError);

    private sealed record CsvSourceRecord(
        int RowNumber,
        IReadOnlyList<string> Values);

    private sealed record CommitCounts(int Created, int Updated, int Unchanged);
}
