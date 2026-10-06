using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Auditing;
using Inventory.Domain.Common;
using Inventory.Domain.Importing;
using Inventory.Domain.Locations;
using Inventory.Domain.Warranties;

namespace Inventory.Application.DataExchange;

public sealed class ImportExportService
{
    private const string ImportedWarrantyType = "Imported";

    private static readonly JsonSerializerOptions SnapshotJsonOptions = CreateJsonOptions();

    private static readonly HashSet<string> AllowedContentTypes = new(
        [
            "text/csv",
            "application/csv",
            "application/vnd.ms-excel",
            "application/octet-stream",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CurrencyCodes = new(
        """
        AED AFN ALL AMD ANG AOA ARS AUD AWG AZN BAM BBD BDT BGN BHD BIF BMD BND
        BOB BOV BRL BSD BTN BWP BYN BZD CAD CDF CHE CHF CHW CLF CLP CNY COP COU
        CRC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR FJD FKP GBP GEL GHS GIP
        GMD GNF GTQ GYD HKD HNL HTG HUF IDR ILS INR IQD IRR ISK JMD JOD JPY KES
        KGS KHR KMF KPW KRW KWD KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD
        MMK MNT MOP MRU MUR MVR MWK MXN MXV MYR MZN NAD NGN NIO NOK NPR NZD OMR
        PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF SAR SBD SCR SDG SEK SGD
        SHP SLE SOS SRD SSP STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS
        UAH UGX USD USN UYI UYU UYW UZS VED VES VND VUV WST XAF XAG XAU XBA XBB
        XBC XBD XCD XCG XDR XOF XPD XPF XPT XSU XTS XUA XXX YER ZAR ZMW ZWG
        """
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries),
        StringComparer.Ordinal);

    private readonly IImportExportRepository _repository;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;
    private readonly ImportExportSettings _settings;
    private readonly IImportPreviewBudget _previewBudget;

    public ImportExportService(
        IImportExportRepository repository,
        IClock clock,
        ICurrentUserContext currentUser,
        ImportExportSettings settings,
        IImportPreviewBudget previewBudget)
    {
        _repository = repository;
        _clock = clock;
        _currentUser = currentUser;
        _settings = settings;
        _previewBudget = previewBudget;
    }

    public async Task<AssetImportJobView> ValidateAssetImportAsync(
        AssetImportFile file,
        AssetImportMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var fileName = ValidateFileMetadata(file);

        var actor = _currentUser.RequireActor();
        var content = await ReadBoundedAsync(file.Content, cancellationToken);
        if (content.Length == 0)
        {
            throw InvalidFile("The CSV file is empty.");
        }

        var fileHash = Hash(content);
        ParsedCsvDocument document;
        try
        {
            document = AssetCsvCodec.Parse(AssetCsvCodec.DecodeStrictUtf8(content));
        }
        catch (CsvFormatException exception)
        {
            return await PersistValidationAsync(
                fileName,
                fileHash,
                mode,
                [],
                [FileError("invalid_csv", exception.Message)],
                totalRows: 0,
                actor,
                cancellationToken);
        }

        if (document.Records.Count == 0)
        {
            return await PersistValidationAsync(
                fileName,
                fileHash,
                mode,
                [],
                [FileError("missing_header", "The CSV header row is missing.")],
                totalRows: 0,
                actor,
                cancellationToken);
        }

        var inputFormat = DetectInputFormat(document.Records[0]);
        if (inputFormat is null)
        {
            return await PersistValidationAsync(
                fileName,
                fileHash,
                mode,
                [],
                [
                    FileError(
                        "invalid_header",
                        "The CSV header does not match canonical schema version 1 or 2, " +
                        "or the supported STR asset-register format."),
                ],
                totalRows: 0,
                actor,
                cancellationToken);
        }

        var dataRecords = document.Records
            .Skip(1)
            .Select((record, index) => new CsvSourceRecord(index + 2, record))
            .Where(source => source.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToArray();
        if (dataRecords.Length == 0)
        {
            return await PersistValidationAsync(
                fileName,
                fileHash,
                mode,
                [],
                [FileError("no_data_rows", "The CSV file contains no asset rows.")],
                totalRows: 0,
                actor,
                cancellationToken);
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

        var errors = new List<AssetImportError>();
        var candidates = new List<CandidateRow>(dataRecords.Length);
        foreach (var source in dataRecords)
        {
            var expectedHeaders = HeadersFor(inputFormat.Value);
            if (source.Values.Count != expectedHeaders.Count)
            {
                errors.Add(
                    new AssetImportError(
                        source.RowNumber,
                        "row",
                        "column_count",
                        $"Expected {expectedHeaders.Count} columns but found " +
                        $"{source.Values.Count}.",
                        null));
                continue;
            }

            candidates.Add(
                ParseCandidate(
                    source.Values,
                    inputFormat.Value,
                    source.RowNumber,
                    fileHash,
                    errors));
        }

        if (inputFormat == CsvInputFormat.StrLegacyRegister)
        {
            AddLegacySchoolErrors(dataRecords, errors);
        }

        AddFileDuplicateErrors(candidates, errors);
        var lookups = await _repository.GetImportLookupsAsync(
            BuildLookupRequest(candidates),
            cancellationToken);
        var validatedRows = ValidateCandidates(candidates, lookups, mode, errors);

        return await PersistValidationAsync(
            fileName,
            fileHash,
            mode,
            validatedRows,
            errors,
            dataRecords.Length,
            actor,
            cancellationToken,
            SchemaVersionFor(inputFormat.Value));
    }

    public async Task<AssetImportJobView> GetImportJobAsync(
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        var job = await _repository.GetImportJobAsync(
            importJobId,
            forUpdate: false,
            cancellationToken)
            ?? throw new ResourceNotFoundException("Import job", importJobId);
        await _previewBudget.EnsureAvailableAsync(
            job.Id, ImportPreviewPayloadKind.Asset, cancellationToken);
        return ToView(job);
    }

    internal async Task<LegacyImportSnapshot> GetLegacyReconciliationSnapshotAsync(
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        var job = await _repository.GetImportJobAsync(
            importJobId,
            forUpdate: false,
            cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Import job",
                importJobId);
        await _previewBudget.EnsureAvailableAsync(
            job.Id, ImportPreviewPayloadKind.Asset, cancellationToken);
        var rows = DeserializeRows(job.ValidatedRowsJson);
        return new LegacyImportSnapshot(
            ToView(job),
            rows
                .Select(
                    row => new LegacyImportSourceRow(
                        row.RowNumber,
                        row.Action,
                        row.AssetId,
                        row.AssetTag,
                        row.DeviceName,
                        row.SerialNumber,
                        row.ResolvedCategoryCode ?? row.Category,
                        row.CategoryWillBeCreated,
                        row.Status,
                        row.ResolvedLocationCode ?? row.LocationCode,
                        row.LocationWillBeCreated,
                        row.AssignedPersonExternalId ?? row.DepartmentCode,
                        row.AssignmentResolution,
                        row.AssignedToDisplayName,
                        row.LegacyId,
                        row.InferredDateFields ?? []))
                .ToArray(),
            DeserializeErrors(job.ErrorsJson));
    }

    public async Task<AssetCsvFile> GetImportErrorsCsvAsync(
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        var job = await _repository.GetImportJobAsync(
            importJobId,
            forUpdate: false,
            cancellationToken)
            ?? throw new ResourceNotFoundException("Import job", importJobId);
        await _previewBudget.EnsureAvailableAsync(
            job.Id, ImportPreviewPayloadKind.Asset, cancellationToken);
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
        return new AssetCsvFile(
            content,
            $"asset-import-{job.Id:N}-errors.csv",
            errors.Count);
    }

    public async Task<AssetImportCommitResult> CommitAssetImportAsync(
        Guid importJobId,
        Guid version,
        CancellationToken cancellationToken)
    {
        var job = await _repository.GetImportJobAsync(
            importJobId,
            forUpdate: true,
            cancellationToken)
            ?? throw new ResourceNotFoundException("Import job", importJobId);
        var actor = _currentUser.RequireActor();

        await _previewBudget.EnsureAvailableAsync(
            job.Id, ImportPreviewPayloadKind.Asset, cancellationToken);

        if (!await _previewBudget.IsWithinRetentionAsync(
                job.Id, ImportPreviewPayloadKind.Asset, cancellationToken))
        {
            throw new ResourceConflictException(
                "import_preview_expired",
                "This import preview has expired. Upload and validate the file again.");
        }

        if (job.Status != AssetImportStatus.ReadyToCommit)
        {
            throw await RecordCommitRejectionAsync(
                job,
                actor,
                "import_not_ready",
                job.Status == AssetImportStatus.Committed
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

        var rows = DeserializeRows(job.ValidatedRowsJson);
        if (rows.Count != job.ValidRows || rows.Count == 0)
        {
            throw await RecordCommitRejectionAsync(
                job,
                actor,
                "import_snapshot_invalid",
                "The validated import snapshot has an inconsistent row count.",
                cancellationToken);
        }

        var state = await _repository.GetImportCommitStateAsync(
            BuildCommitLookupRequest(rows),
            cancellationToken);
        try
        {
            EnsureSnapshotIsCurrent(rows, state);
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

        var materializedReferences = MaterializeImportReferences(rows, state);
        var commitRows = materializedReferences.Rows;
        var now = _clock.UtcNow;
        var assets = state.Assets.ToDictionary(asset => asset.Id);
        var people = state.People.ToDictionary(person => person.Id);
        var departments = state.Departments.ToDictionary(department => department.Id);
        var assignments = state.Assignments.ToDictionary(assignment => assignment.AssetId);
        var warranties = state.Warranties.ToDictionary(warranty => warranty.AssetId);
        var createdRows = 0;
        var updatedRows = 0;
        var unchangedRows = 0;

        foreach (var row in commitRows.OrderBy(row => row.RowNumber))
        {
            var changedFields = new HashSet<string>(StringComparer.Ordinal);
            Asset asset;
            if (row.Action == AssetImportAction.Create)
            {
                asset = CreateAsset(row, now);
                _repository.AddAsset(asset);
                createdRows++;
                changedFields.Add("asset");
            }
            else
            {
                asset = assets[row.AssetId!.Value];
            }

            assignments.TryGetValue(asset.Id, out var currentAssignment);
            ApplyAssetAndAssignment(
                row,
                asset,
                currentAssignment,
                people,
                departments,
                actor,
                now,
                changedFields);

            warranties.TryGetValue(asset.Id, out var warranty);
            ApplyWarranty(row, asset, warranty, now, changedFields);

            if (row.Action == AssetImportAction.Update)
            {
                if (changedFields.Count == 0)
                {
                    unchangedRows++;
                    continue;
                }

                updatedRows++;
            }

            _repository.AddAssetEvent(
                AssetEvent.Create(
                    asset.Id,
                    row.Action == AssetImportAction.Create
                        ? AssetEventType.ImportCreated
                        : AssetEventType.ImportUpdated,
                    now,
                    actor.UserId,
                    actor.DisplayName,
                    AssetEventSource.Import,
                    row.Action == AssetImportAction.Create
                        ? $"Asset {asset.AssetTag} was created by import."
                        : $"Asset {asset.AssetTag} was updated by import.",
                    JsonSerializer.Serialize(
                        new
                        {
                            importJobId = job.Id,
                            rowNumber = row.RowNumber,
                            action = row.Action,
                            row.AssetTagGenerated,
                            categoryCreated = row.CategoryWillBeCreated,
                            locationCreated = row.LocationWillBeCreated,
                            assignmentResolution = row.AssignmentResolution,
                            inferredDateFields = row.InferredDateFields ?? [],
                            changedFields = changedFields.Order(StringComparer.Ordinal),
                        },
                        SnapshotJsonOptions),
                    actor.CorrelationId));
        }

        try
        {
            job.Commit(
                version,
                now,
                actor.UserId,
                actor.DisplayName,
                createdRows,
                updatedRows,
                unchangedRows);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _repository.AddAuditLog(
            CreateAudit(
                actor,
                "assets.import.committed",
                "asset_import_job",
                job.Id.ToString(),
                "succeeded",
                new
                {
                    job.FileHash,
                    job.Mode,
                    createdRows,
                    updatedRows,
                    unchangedRows,
                    generatedAssetTagRows =
                        commitRows.Count(row => row.AssetTagGenerated),
                    createdCategories = materializedReferences.CreatedCategories
                        .Select(category => category.Code)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    createdLocations = materializedReferences.CreatedLocations
                        .Select(location => location.Code)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    inferredDateRows =
                        commitRows.Count(row => (row.InferredDateFields?.Count ?? 0) > 0),
                    fuzzyPersonMatchRows = commitRows.Count(
                        row =>
                            row.AssignmentResolution ==
                            AssetImportAssignmentResolution.FuzzyPerson),
                    unassignedImportRows = commitRows.Count(
                        row =>
                            row.AssignmentResolution ==
                            AssetImportAssignmentResolution.Unassigned),
                }));
        try
        {
            await SaveChangesAsync(cancellationToken);
        }
        catch (ResourceConflictException exception)
        {
            var rejectionAudit = CreateAudit(
                actor,
                "assets.import.commit_rejected",
                "asset_import_job",
                job.Id.ToString(),
                "rejected",
                new
                {
                    code = exception.Code,
                    job.FileHash,
                    job.Mode,
                    status = AssetImportStatus.ReadyToCommit,
                    reason = "late_persistence_conflict",
                });

            try
            {
                await _repository.SaveRejectedCommitAuditAfterFailureAsync(
                    rejectionAudit,
                    cancellationToken);
            }
            catch (PersistenceConflictException auditException)
            {
                throw new InvalidOperationException(
                    "The import commit failed and its rejection audit could not be persisted.",
                    auditException);
            }

            throw;
        }

        return new AssetImportCommitResult(
            job.Id,
            job.Status,
            createdRows,
            updatedRows,
            unchangedRows,
            materializedReferences.CreatedCategories.Count,
            materializedReferences.CreatedLocations.Count,
            now,
            job.Version);
    }

    public async Task<AssetCsvFile> ExportAssetsAsync(
        AssetExportFilter filter,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeExportFilter(filter);
        var rows = await _repository.ExportAssetsAsync(
            normalized,
            _settings.MaxExportRows + 1,
            cancellationToken);
        var actor = _currentUser.RequireActor();

        if (rows.Count > _settings.MaxExportRows)
        {
            _repository.AddAuditLog(
                CreateAudit(
                    actor,
                    "assets.exported",
                    "asset",
                    null,
                    "rejected",
                    new
                    {
                        filter = normalized,
                        limit = _settings.MaxExportRows,
                        reason = "row_limit",
                    }));
            await SaveChangesAsync(cancellationToken);
            throw new RequestValidationException(
                "export_row_limit_exceeded",
                $"The export matches more than {_settings.MaxExportRows} assets. " +
                "Apply narrower filters and retry.");
        }

        var csvRows = rows.Select(ToCsvFields);
        var content = AssetCsvCodec.Write(AssetCsvSchema.Headers, csvRows);
        _repository.AddAuditLog(
            CreateAudit(
                actor,
                "assets.exported",
                "asset",
                null,
                "succeeded",
                new
                {
                    filter = normalized,
                    rowCount = rows.Count,
                    schemaVersion = ImportExportSettings.SupportedSchemaVersion,
                }));
        await SaveChangesAsync(cancellationToken);

        return new AssetCsvFile(
            content,
            $"assets-{_clock.UtcNow:yyyyMMdd-HHmmss}.csv",
            rows.Count);
    }

    private async Task<AssetImportJobView> PersistValidationAsync(
        string fileName,
        string fileHash,
        AssetImportMode mode,
        List<ValidatedAssetImportRow> rows,
        List<AssetImportError> errors,
        int totalRows,
        CurrentActor actor,
        CancellationToken cancellationToken,
        int schemaVersion = ImportExportSettings.SupportedSchemaVersion)
    {
        var errorRowCount = errors
            .Where(error => error.RowNumber >= 2)
            .Select(error => error.RowNumber)
            .Distinct()
            .Count();
        if (rows.Count + errorRowCount != totalRows)
        {
            throw new InvalidOperationException(
                "Import validation did not account for every data row.");
        }

        var rowsJson = JsonSerializer.Serialize(rows, SnapshotJsonOptions);
        var errorsJson = JsonSerializer.Serialize(errors, SnapshotJsonOptions);
        var now = _clock.UtcNow;
        AssetImportJob job;
        try
        {
            job = AssetImportJob.CreateValidation(
                schemaVersion,
                fileName,
                fileHash,
                Hash(Encoding.UTF8.GetBytes(rowsJson)),
                mode,
                totalRows,
                rows.Count,
                errorRowCount,
                rows.Count(row => row.Action == AssetImportAction.Create),
                rows.Count(row => row.Action == AssetImportAction.Update),
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
            ImportPreviewPayloadKind.Asset,
            actor.UserId,
            payloadBytes,
            now,
            async token =>
            {
                _repository.AddImportJob(job);
                _repository.AddAuditLog(
            CreateAudit(
                actor,
                "assets.import.validated",
                "asset_import_job",
                job.Id.ToString(),
                errors.Count == 0 ? "succeeded" : "validation_failed",
                new
                {
                    fileName,
                    fileHash,
                    schemaVersion,
                    mode,
                    totalRows,
                    validRows = rows.Count,
                    errorRows = errorRowCount,
                    errorCount = errors.Count,
                    generatedAssetTagRows =
                        rows.Count(row => row.AssetTagGenerated),
                    plannedCategoryCreates = rows
                        .Where(row => row.CategoryWillBeCreated)
                        .Select(row => row.ResolvedCategoryCode)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    plannedLocationCreates = rows
                        .Where(row => row.LocationWillBeCreated)
                        .Select(row => row.ResolvedLocationCode)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    inferredDateRows =
                        rows.Count(row => (row.InferredDateFields?.Count ?? 0) > 0),
                    fuzzyPersonMatchRows = rows.Count(
                        row =>
                            row.AssignmentResolution ==
                            AssetImportAssignmentResolution.FuzzyPerson),
                    unassignedImportRows = rows.Count(
                        row =>
                            row.AssignmentResolution ==
                            AssetImportAssignmentResolution.Unassigned),
                }));
                await SaveChangesAsync(token);
                return ToView(job);
            },
            cancellationToken);
    }

    private static List<ValidatedAssetImportRow> ValidateCandidates(
        IReadOnlyList<CandidateRow> candidates,
        ImportLookupSnapshot lookups,
        AssetImportMode mode,
        List<AssetImportError> errors)
    {
        var categoriesByCode = lookups.Categories
            .GroupBy(category => NormalizeKey(category.Code))
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var categoriesByName = lookups.Categories
            .GroupBy(category => NormalizeKey(category.Name))
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var locationsByCode = lookups.Locations
            .GroupBy(location => NormalizeKey(location.Code))
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var locationsByName = lookups.Locations
            .GroupBy(location => NormalizeKey(location.Name))
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var departmentsByCode = lookups.Departments
            .GroupBy(department => NormalizeKey(department.Code))
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var peopleByExternalId = lookups.People
            .GroupBy(person => NormalizeKey(person.ExternalId))
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var assetsByTag = lookups.Assets
            .GroupBy(asset => asset.AssetTagNormalized)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var assetsBySerial = lookups.Assets
            .Where(asset => asset.SerialNumberNormalized is not null)
            .GroupBy(asset => asset.SerialNumberNormalized!)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var reservedCategoryCodes = lookups.Categories
            .Select(category => NormalizeKey(category.Code))
            .ToHashSet(StringComparer.Ordinal);
        var reservedLocationCodes = lookups.Locations
            .Select(location => NormalizeKey(location.Code))
            .ToHashSet(StringComparer.Ordinal);
        var plannedCategories = new Dictionary<string, ImportReferenceResolution>(
            StringComparer.Ordinal);
        var plannedLocations = new Dictionary<string, ImportReferenceResolution>(
            StringComparer.Ordinal);

        var validated = new List<ValidatedAssetImportRow>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var hadExistingErrors = errors.Any(
                error => error.RowNumber == candidate.RowNumber);
            var category = ResolveCategory(
                candidate,
                categoriesByCode,
                categoriesByName,
                plannedCategories,
                reservedCategoryCodes,
                errors);
            var location = ResolveLocation(
                candidate,
                locationsByCode,
                locationsByName,
                plannedLocations,
                reservedLocationCodes,
                errors);
            var department = ResolveDepartment(candidate, departmentsByCode);
            var personResolution = ResolvePerson(
                candidate,
                peopleByExternalId,
                lookups.People);
            var person = personResolution.Person;

            var departmentSourceSupplied = candidate.DepartmentCode is not null;
            var personSourceSupplied = candidate.AssignedPersonExternalId is not null;
            if (departmentSourceSupplied && personSourceSupplied)
            {
                AddError(
                    errors,
                    candidate.RowNumber,
                    "assignedPersonExternalId",
                    "multiple_assignment_targets",
                    "Supply either departmentCode or assignedPersonExternalId, not both.",
                    candidate.AssignedPersonExternalId);
            }

            var hasTarget = department is not null || person is not null;
            if (candidate.Status != AssetStatus.Assigned &&
                (departmentSourceSupplied || personSourceSupplied))
            {
                AddError(
                    errors,
                    candidate.RowNumber,
                    "status",
                    "assigned_status_required",
                    "An assignment target can only be supplied when status is Assigned.",
                    candidate.Status.ToString());
            }

            var effectiveStatus =
                candidate.Status == AssetStatus.Assigned && !hasTarget
                    ? AssetStatus.InStock
                    : candidate.Status;
            var assignmentResolution = department is not null
                ? AssetImportAssignmentResolution.Department
                : person is not null
                    ? personResolution.Resolution
                    : candidate.Status == AssetStatus.Assigned
                        ? AssetImportAssignmentResolution.Unassigned
                        : AssetImportAssignmentResolution.None;

            if (candidate.Status is AssetStatus.Retired or AssetStatus.Disposed)
            {
                AddError(
                    errors,
                    candidate.RowNumber,
                    "status",
                    "lifecycle_action_required",
                    "Retired and disposed states cannot be applied through an asset import.",
                    candidate.Status.ToString());
            }

            assetsByTag.TryGetValue(
                candidate.AssetTagNormalized ?? string.Empty,
                out var tagMatches);
            var matchedAsset = tagMatches?.SingleOrDefault();
            if (mode == AssetImportMode.CreateOnly && matchedAsset is not null)
            {
                AddError(
                    errors,
                    candidate.RowNumber,
                    "assetTag",
                    "duplicate_asset_tag",
                    "The asset tag is already in use.",
                    candidate.AssetTag);
            }

            if (mode == AssetImportMode.UpsertByAssetTag &&
                matchedAsset?.Status is AssetStatus.Retired or AssetStatus.Disposed)
            {
                AddError(
                    errors,
                    candidate.RowNumber,
                    "assetTag",
                    "asset_not_mutable",
                    "Retired or disposed assets cannot be updated by import.",
                    candidate.AssetTag);
            }

            if (candidate.SerialNumberNormalized is not null &&
                assetsBySerial.TryGetValue(
                    candidate.SerialNumberNormalized,
                    out var serialMatches) &&
                serialMatches.Any(asset => asset.Id != matchedAsset?.Id))
            {
                AddError(
                    errors,
                    candidate.RowNumber,
                    "serialNumber",
                    "duplicate_serial_number",
                    "The serial number is already in use by another asset.",
                    candidate.SerialNumber);
            }

            if (hadExistingErrors ||
                errors.Any(error => error.RowNumber == candidate.RowNumber) ||
                category is null ||
                candidate.AssetTagNormalized is null)
            {
                continue;
            }

            var action = mode == AssetImportMode.UpsertByAssetTag && matchedAsset is not null
                ? AssetImportAction.Update
                : AssetImportAction.Create;
            validated.Add(
                new ValidatedAssetImportRow(
                    candidate.RowNumber,
                    action,
                    candidate.PreserveVersion2Fields,
                    candidate.AssetTagGenerated,
                    matchedAsset?.Id,
                    matchedAsset?.Version,
                    matchedAsset?.Assignment?.Id,
                    matchedAsset?.Assignment?.Version,
                    matchedAsset?.Warranty?.Id,
                    matchedAsset?.Warranty?.Version,
                    category.Id,
                    category.Version,
                    location?.Id,
                    location?.Version,
                    person?.Id,
                    person?.Version,
                    department?.Id,
                    department?.Version,
                    candidate.AssetTag!,
                    candidate.AssetTagNormalized,
                    candidate.DeviceName,
                    candidate.Category!,
                    candidate.Description,
                    candidate.Manufacturer,
                    candidate.Model,
                    candidate.SerialNumber,
                    candidate.SerialNumberNormalized,
                    candidate.LanMacAddress,
                    candidate.BiosGuid,
                    candidate.Ownership,
                    effectiveStatus,
                    candidate.Condition,
                    candidate.LocationCode,
                    candidate.DepartmentCode,
                    candidate.AssignedPersonExternalId,
                    person?.DisplayName ?? department?.Name,
                    candidate.PurchaseDate,
                    candidate.PurchaseCost,
                    candidate.Currency,
                    candidate.Supplier,
                    candidate.WarrantyStartDate,
                    candidate.WarrantyEndDate,
                    candidate.WarrantyProvider,
                    candidate.WarrantyReference,
                    candidate.OperatingSystem,
                    candidate.OperatingSystemVersion,
                    candidate.Windows11Compliant,
                    candidate.LegacyId,
                    candidate.Notes,
                    category.Code,
                    category.Name,
                    category.WillBeCreated,
                    location?.Code,
                    location?.Name,
                    location?.WillBeCreated ?? false,
                    candidate.InferredDateFields,
                    assignmentResolution));
        }

        return validated;
    }

    private CandidateRow ParseCandidate(
        IReadOnlyList<string> record,
        CsvInputFormat inputFormat,
        int rowNumber,
        string fileHash,
        List<AssetImportError> errors)
    {
        var canonicalRecord = inputFormat == CsvInputFormat.StrLegacyRegister
            ? MapLegacyRecord(record, rowNumber, errors)
            : record;
        var headers = inputFormat == CsvInputFormat.CanonicalVersion1
            ? AssetCsvSchema.Version1Headers
            : AssetCsvSchema.Headers;
        var values = headers
            .Select(
                (header, index) => new
                {
                    Header = header,
                    Value = AssetCsvCodec.RemoveFormulaProtection(canonicalRecord[index]),
                })
            .ToDictionary(item => item.Header, item => item.Value, StringComparer.Ordinal);
        string Value(string column) => values.GetValueOrDefault(column) ?? string.Empty;

        var schemaVersion = SchemaVersionFor(inputFormat);
        var schema = Required(Value("schemaVersion"), 10, rowNumber, "schemaVersion", errors);
        if (schema is not null &&
            schema != schemaVersion.ToString(CultureInfo.InvariantCulture))
        {
            AddError(
                errors,
                rowNumber,
                "schemaVersion",
                "unsupported_schema_version",
                $"This header requires asset CSV schema version {schemaVersion}.",
                schema);
        }

        var sourceAssetTag = Value("assetTag");
        var assetTagGenerated = string.IsNullOrWhiteSpace(sourceAssetTag);
        var assetTag = assetTagGenerated
            ? GenerateAssetTag(fileHash, rowNumber)
            : Required(sourceAssetTag, 100, rowNumber, "assetTag", errors);
        var deviceName = Optional(Value("deviceName"), 150, rowNumber, "deviceName", errors);
        var category = Required(Value("category"), 150, rowNumber, "category", errors);
        var description = Optional(
            Value("description"),
            2000,
            rowNumber,
            "description",
            errors);
        var manufacturer = Optional(
            Value("manufacturer"),
            150,
            rowNumber,
            "manufacturer",
            errors);
        var model = Optional(Value("model"), 150, rowNumber, "model", errors);
        var serialNumber = Optional(
            Value("serialNumber"),
            200,
            rowNumber,
            "serialNumber",
            errors);
        var lanMacAddress = ParseMacAddress(
            Value("lanMacAddress"),
            rowNumber,
            errors);
        var biosGuid = ParseGuid(Value("biosGuid"), rowNumber, "biosGuid", errors);
        var ownership = Optional(Value("ownership"), 100, rowNumber, "ownership", errors);
        var status = ParseEnum(
            Value("status"),
            AssetStatus.InStock,
            rowNumber,
            "status",
            errors);
        var condition = ParseEnum(
            Value("condition"),
            AssetCondition.Good,
            rowNumber,
            "condition",
            errors);
        var locationCode = Optional(
            Value("locationCode"),
            150,
            rowNumber,
            "locationCode",
            errors);
        var departmentCode = Optional(
            Value("departmentCode"),
            50,
            rowNumber,
            "departmentCode",
            errors);
        var personExternalId = Optional(
            Value("assignedPersonExternalId"),
            100,
            rowNumber,
            "assignedPersonExternalId",
            errors);
        var inferredDateFields = new List<string>(3);
        var purchaseDate = ParseDate(
            Value("purchaseDate"),
            rowNumber,
            "purchaseDate",
            errors,
            allowLegacyDateFormats: inputFormat == CsvInputFormat.StrLegacyRegister,
            partialDateBoundary: PartialDateBoundary.Start,
            inferredDateFields: inferredDateFields);
        var purchaseCost = ParseDecimal(
            Value("purchaseCost"),
            rowNumber,
            "purchaseCost",
            errors);
        var currency = Optional(Value("currency"), 3, rowNumber, "currency", errors)
            ?.ToUpperInvariant();
        var supplier = Optional(Value("supplier"), 200, rowNumber, "supplier", errors);
        var warrantyStartDate = ParseDate(
            Value("warrantyStartDate"),
            rowNumber,
            "warrantyStartDate",
            errors,
            allowLegacyDateFormats: inputFormat == CsvInputFormat.StrLegacyRegister,
            partialDateBoundary: PartialDateBoundary.Start,
            inferredDateFields: inferredDateFields);
        var warrantyEndDate = ParseDate(
            Value("warrantyEndDate"),
            rowNumber,
            "warrantyEndDate",
            errors,
            allowLegacyDateFormats: inputFormat == CsvInputFormat.StrLegacyRegister,
            partialDateBoundary: PartialDateBoundary.End,
            inferredDateFields: inferredDateFields);
        var warrantyProvider = Optional(
            Value("warrantyProvider"),
            200,
            rowNumber,
            "warrantyProvider",
            errors);
        var warrantyReference = Optional(
            Value("warrantyReference"),
            200,
            rowNumber,
            "warrantyReference",
            errors);
        var operatingSystem = Optional(
            Value("operatingSystem"),
            100,
            rowNumber,
            "operatingSystem",
            errors);
        var operatingSystemVersion = Optional(
            Value("operatingSystemVersion"),
            100,
            rowNumber,
            "operatingSystemVersion",
            errors);
        var windows11Compliant = ParseNullableBoolean(
            Value("windows11Compliant"),
            rowNumber,
            "windows11Compliant",
            errors);
        var legacyId = Optional(Value("legacyId"), 200, rowNumber, "legacyId", errors);
        var notes = Optional(Value("notes"), 4000, rowNumber, "notes", errors);

        if (purchaseCost < 0 || purchaseCost > 9999999999999999.99m)
        {
            AddError(
                errors,
                rowNumber,
                "purchaseCost",
                "invalid_purchase_cost",
                "Purchase cost must be between 0 and 9999999999999999.99.",
                Value("purchaseCost"));
        }

        if (purchaseCost.HasValue && currency is null)
        {
            AddError(
                errors,
                rowNumber,
                "currency",
                "currency_required",
                "Currency is required when purchaseCost is supplied.",
                Value("currency"));
        }

        if (currency is not null && !CurrencyCodes.Contains(currency))
        {
            AddError(
                errors,
                rowNumber,
                "currency",
                "invalid_currency",
                "Currency must be a supported ISO 4217 code.",
                currency);
        }

        if (warrantyStartDate.HasValue &&
            warrantyEndDate.HasValue &&
            warrantyEndDate.Value < warrantyStartDate.Value)
        {
            AddError(
                errors,
                rowNumber,
                "warrantyEndDate",
                "invalid_warranty_dates",
                "Warranty end date cannot precede its start date.",
                Value("warrantyEndDate"));
        }

        var hasWarranty = warrantyStartDate.HasValue ||
                          warrantyEndDate.HasValue ||
                          warrantyProvider is not null ||
                          warrantyReference is not null;
        if (hasWarranty && warrantyProvider is null)
        {
            AddError(
                errors,
                rowNumber,
                "warrantyProvider",
                "warranty_provider_required",
                "Warranty provider is required when warranty data is supplied.",
                Value("warrantyProvider"));
        }

        return new CandidateRow(
            rowNumber,
            inputFormat == CsvInputFormat.CanonicalVersion1,
            assetTagGenerated,
            assetTag,
            assetTag is null ? null : NormalizeKey(assetTag),
            deviceName,
            category,
            description,
            manufacturer,
            model,
            serialNumber,
            serialNumber is null ? null : NormalizeKey(serialNumber),
            lanMacAddress,
            biosGuid,
            ownership,
            status,
            condition,
            locationCode,
            departmentCode,
            personExternalId,
            purchaseDate,
            purchaseCost,
            currency,
            supplier,
            warrantyStartDate,
            warrantyEndDate,
            warrantyProvider,
            warrantyReference,
            operatingSystem,
            operatingSystemVersion,
            windows11Compliant,
            legacyId,
            notes,
            inferredDateFields);
    }

    private IReadOnlyList<string> MapLegacyRecord(
        IReadOnlyList<string> record,
        int rowNumber,
        List<AssetImportError> errors)
    {
        string Field(int index) => AssetCsvCodec.RemoveFormulaProtection(record[index]).Trim();

        var deviceName = Field(0);
        var acquisitionDate = Field(14);
        var warrantyEndDate = Field(15);
        var usedBy = Field(6);
        var purchaseCost = Field(12);
        var warrantyIndicator = Field(13);
        if (warrantyIndicator.Length > 0 &&
            !warrantyIndicator.Equals("yes", StringComparison.OrdinalIgnoreCase) &&
            !warrantyIndicator.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            AddError(
                errors,
                rowNumber,
                "Warranty",
                "invalid_warranty_indicator",
                "Warranty must be Yes, No, or blank.",
                warrantyIndicator);
        }

        var hasWarrantyDates = acquisitionDate.Length > 0 || warrantyEndDate.Length > 0;
        return
        [
            ImportExportSettings.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture),
            deviceName,
            deviceName,
            Field(1),
            Field(2),
            Field(10),
            Field(9),
            Field(3),
            Field(4),
            Field(5),
            Field(11),
            MapLegacyStatus(Field(16), usedBy.Length > 0),
            AssetCondition.Good.ToString(),
            Field(8),
            string.Empty,
            usedBy,
            acquisitionDate,
            purchaseCost,
            purchaseCost.Length == 0
                ? string.Empty
                : _settings.LegacyDefaultCurrency.Trim().ToUpperInvariant(),
            string.Empty,
            hasWarrantyDates ? acquisitionDate : string.Empty,
            hasWarrantyDates ? warrantyEndDate : string.Empty,
            hasWarrantyDates ? "Legacy asset register" : string.Empty,
            string.Empty,
            Field(17),
            Field(18),
            Field(19),
            string.Empty,
            string.Empty,
        ];
    }

    private static string MapLegacyStatus(string value, bool hasPerson)
    {
        var normalized = string.Join(
            ' ',
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0 ||
            normalized.Equals("in use", StringComparison.OrdinalIgnoreCase))
        {
            return hasPerson ? AssetStatus.Assigned.ToString() : AssetStatus.InStock.ToString();
        }

        return normalized.ToUpperInvariant() switch
        {
            "IN STOCK" or "STOCK" => AssetStatus.InStock.ToString(),
            "IN REPAIR" => AssetStatus.Repair.ToString(),
            _ => normalized.Replace(" ", string.Empty, StringComparison.Ordinal),
        };
    }

    private static void AddLegacySchoolErrors(
        IReadOnlyList<CsvSourceRecord> records,
        List<AssetImportError> errors)
    {
        var recordsWithSchoolColumn = records
            .Where(record => record.Values.Count > 7)
            .ToArray();
        var schoolCodes = recordsWithSchoolColumn
            .Select(record => record.Values[7].Trim())
            .Where(value => value.Length > 0)
            .Select(NormalizeKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var record in recordsWithSchoolColumn.Where(record =>
                     string.IsNullOrWhiteSpace(record.Values[7])))
        {
            AddError(
                errors,
                record.RowNumber,
                "School",
                "required",
                "School is required in the STR legacy register.",
                record.Values[7]);
        }

        if (schoolCodes.Length <= 1)
        {
            return;
        }

        foreach (var record in recordsWithSchoolColumn)
        {
            AddError(
                errors,
                record.RowNumber,
                "School",
                "multiple_schools_not_supported",
                "A single import file cannot contain more than one school code.",
                record.Values[7]);
        }
    }

    private static void AddFileDuplicateErrors(
        IReadOnlyList<CandidateRow> candidates,
        List<AssetImportError> errors)
    {
        foreach (var duplicate in candidates
                     .Where(row => row.AssetTagNormalized is not null)
                     .GroupBy(row => row.AssetTagNormalized!, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            foreach (var row in duplicate)
            {
                AddError(
                    errors,
                    row.RowNumber,
                    "assetTag",
                    "duplicate_asset_tag_in_file",
                    "The asset tag occurs more than once in this CSV.",
                    row.AssetTag);
            }
        }

        foreach (var duplicate in candidates
                     .Where(row => row.SerialNumberNormalized is not null)
                     .GroupBy(row => row.SerialNumberNormalized!, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            foreach (var row in duplicate)
            {
                AddError(
                    errors,
                    row.RowNumber,
                    "serialNumber",
                    "duplicate_serial_number_in_file",
                    "The serial number occurs more than once in this CSV.",
                    row.SerialNumber);
            }
        }
    }

    private static ImportLookupRequest BuildLookupRequest(
        IReadOnlyList<CandidateRow> candidates) =>
        new(
            candidates
                .Where(row => row.Category is not null)
                .Select(row => NormalizeKey(row.Category!))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            candidates
                .Where(row => row.LocationCode is not null)
                .Select(row => NormalizeKey(row.LocationCode!))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            candidates
                .Where(row => row.DepartmentCode is not null)
                .Select(row => NormalizeKey(row.DepartmentCode!))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            candidates
                .Where(row => row.AssignedPersonExternalId is not null)
                .Select(row => NormalizeKey(row.AssignedPersonExternalId!))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            candidates
                .Where(row => row.AssetTagNormalized is not null)
                .Select(row => row.AssetTagNormalized!)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            candidates
                .Where(row => row.SerialNumberNormalized is not null)
                .Select(row => row.SerialNumberNormalized!)
                .Distinct(StringComparer.Ordinal)
                .ToArray());

    private static ImportCommitLookupRequest BuildCommitLookupRequest(
        IReadOnlyList<ValidatedAssetImportRow> rows) =>
        new(
            rows
                .Where(row => row.AssetId.HasValue)
                .Select(row => row.AssetId!.Value)
                .Distinct()
                .ToArray(),
            rows.Select(row => row.AssetTagNormalized)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            rows
                .Where(row => row.SerialNumberNormalized is not null)
                .Select(row => row.SerialNumberNormalized!)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            rows.Where(row => row.CategoryId.HasValue)
                .Select(row => row.CategoryId!.Value)
                .Distinct()
                .ToArray(),
            rows.Where(row => row.CategoryWillBeCreated)
                .SelectMany(
                    row => new[]
                    {
                        NormalizeKey(row.ResolvedCategoryCode ?? row.Category),
                        NormalizeKey(row.ResolvedCategoryName ?? row.Category),
                    })
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            rows.Where(row => row.LocationId.HasValue)
                .Select(row => row.LocationId!.Value)
                .Distinct()
                .ToArray(),
            rows.Where(row => row.LocationWillBeCreated)
                .SelectMany(
                    row => new[]
                    {
                        NormalizeKey(row.ResolvedLocationCode ?? row.LocationCode!),
                        NormalizeKey(row.ResolvedLocationName ?? row.LocationCode!),
                    })
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            rows.Where(row => row.PersonId.HasValue)
                .Select(row => row.PersonId!.Value)
                .Distinct()
                .ToArray(),
            rows.Where(row => row.DepartmentId.HasValue)
                .Select(row => row.DepartmentId!.Value)
                .Distinct()
                .ToArray());

    private static void EnsureSnapshotIsCurrent(
        IReadOnlyList<ValidatedAssetImportRow> rows,
        ImportCommitState state)
    {
        var assetsById = state.Assets.ToDictionary(asset => asset.Id);
        var categories = state.Categories.ToDictionary(category => category.Id);
        var locations = state.Locations.ToDictionary(location => location.Id);
        var people = state.People.ToDictionary(person => person.Id);
        var departments = state.Departments.ToDictionary(department => department.Id);
        var assignments = state.Assignments.ToDictionary(assignment => assignment.AssetId);
        var warranties = state.Warranties.ToDictionary(warranty => warranty.AssetId);

        foreach (var row in rows)
        {
            if (row.CategoryId.HasValue &&
                (!categories.TryGetValue(row.CategoryId.Value, out var category) ||
                 !category.IsActive ||
                 category.Version != row.ExpectedCategoryVersion))
            {
                ThrowStale(row.RowNumber, "category");
            }

            if (row.CategoryWillBeCreated)
            {
                EnsurePlannedCategoryIsSafe(row, state.Categories);
            }

            if (row.LocationId.HasValue &&
                (!locations.TryGetValue(row.LocationId.Value, out var location) ||
                 !location.IsActive ||
                 location.Version != row.ExpectedLocationVersion))
            {
                ThrowStale(row.RowNumber, "location");
            }

            if (row.LocationWillBeCreated)
            {
                EnsurePlannedLocationIsSafe(row, state.Locations);
            }

            if (row.PersonId.HasValue &&
                (!people.TryGetValue(row.PersonId.Value, out var person) ||
                 !person.IsActive ||
                 person.Version != row.ExpectedPersonVersion))
            {
                ThrowStale(row.RowNumber, "person");
            }

            if (row.DepartmentId.HasValue &&
                (!departments.TryGetValue(row.DepartmentId.Value, out var department) ||
                 !department.IsActive ||
                 department.Version != row.ExpectedDepartmentVersion))
            {
                ThrowStale(row.RowNumber, "department");
            }

            Asset? matchedAsset = null;
            if (row.AssetId.HasValue)
            {
                if (!assetsById.TryGetValue(row.AssetId.Value, out matchedAsset) ||
                    matchedAsset.Version != row.ExpectedAssetVersion ||
                    matchedAsset.Status is AssetStatus.Retired or AssetStatus.Disposed)
                {
                    ThrowStale(row.RowNumber, "asset");
                }

                assignments.TryGetValue(row.AssetId.Value, out var assignment);
                if (assignment?.Id != row.ExpectedAssignmentId ||
                    assignment?.Version != row.ExpectedAssignmentVersion)
                {
                    ThrowStale(row.RowNumber, "assignment");
                }

                warranties.TryGetValue(row.AssetId.Value, out var warranty);
                if (warranty?.Id != row.ExpectedWarrantyId ||
                    warranty?.Version != row.ExpectedWarrantyVersion)
                {
                    ThrowStale(row.RowNumber, "warranty");
                }
            }

            if (state.Assets.Any(
                    asset =>
                        asset.Id != matchedAsset?.Id &&
                        asset.AssetTagNormalized == row.AssetTagNormalized))
            {
                ThrowIdentifierConflict(row.RowNumber, "assetTag");
            }

            if (row.SerialNumberNormalized is not null &&
                state.Assets.Any(
                    asset =>
                        asset.Id != matchedAsset?.Id &&
                        asset.SerialNumberNormalized == row.SerialNumberNormalized))
            {
                ThrowIdentifierConflict(row.RowNumber, "serialNumber");
            }
        }
    }

    private static void EnsurePlannedCategoryIsSafe(
        ValidatedAssetImportRow row,
        IReadOnlyList<AssetCategory> categories)
    {
        var code = row.ResolvedCategoryCode ?? row.Category;
        var name = row.ResolvedCategoryName ?? row.Category;
        var codeKey = NormalizeKey(code);
        var nameKey = NormalizeKey(name);
        var matches = categories
            .Where(
                category =>
                    category.CodeNormalized == codeKey ||
                    NormalizeKey(category.Name) == nameKey)
            .DistinctBy(category => category.Id)
            .ToArray();
        if (matches.Length == 0)
        {
            return;
        }

        if (matches.Length != 1 || !matches[0].IsActive)
        {
            ThrowStale(row.RowNumber, "category");
        }

        if (NormalizeKey(matches[0].Name) != nameKey)
        {
            ThrowIdentifierConflict(row.RowNumber, "category");
        }
    }

    private static void EnsurePlannedLocationIsSafe(
        ValidatedAssetImportRow row,
        IReadOnlyList<Location> locations)
    {
        var code = row.ResolvedLocationCode ?? row.LocationCode!;
        var name = row.ResolvedLocationName ?? row.LocationCode!;
        var codeKey = NormalizeKey(code);
        var nameKey = NormalizeKey(name);
        var matches = locations
            .Where(
                location =>
                    location.CodeNormalized == codeKey ||
                    NormalizeKey(location.Name) == nameKey)
            .DistinctBy(location => location.Id)
            .ToArray();
        if (matches.Length == 0)
        {
            return;
        }

        if (matches.Length != 1 || !matches[0].IsActive)
        {
            ThrowStale(row.RowNumber, "location");
        }

        if (NormalizeKey(matches[0].Name) != nameKey)
        {
            ThrowIdentifierConflict(row.RowNumber, "location");
        }
    }

    private MaterializedImportReferences MaterializeImportReferences(
        IReadOnlyList<ValidatedAssetImportRow> rows,
        ImportCommitState state)
    {
        var categoryIdsByPlanCode = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var locationIdsByPlanCode = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var createdCategories = new List<AssetCategory>();
        var createdLocations = new List<Location>();

        foreach (var row in rows.Where(row => row.CategoryWillBeCreated))
        {
            var code = row.ResolvedCategoryCode ?? row.Category;
            var codeKey = NormalizeKey(code);
            if (categoryIdsByPlanCode.ContainsKey(codeKey))
            {
                continue;
            }

            var name = row.ResolvedCategoryName ?? row.Category;
            var nameKey = NormalizeKey(name);
            var existing = state.Categories.SingleOrDefault(
                category =>
                    category.CodeNormalized == codeKey ||
                    NormalizeKey(category.Name) == nameKey);
            if (existing is not null)
            {
                categoryIdsByPlanCode.Add(codeKey, existing.Id);
                continue;
            }

            var category = AssetCategory.Create(code, name);
            _repository.AddCategory(category);
            categoryIdsByPlanCode.Add(codeKey, category.Id);
            createdCategories.Add(category);
        }

        foreach (var row in rows.Where(row => row.LocationWillBeCreated))
        {
            var code = row.ResolvedLocationCode ?? row.LocationCode!;
            var codeKey = NormalizeKey(code);
            if (locationIdsByPlanCode.ContainsKey(codeKey))
            {
                continue;
            }

            var name = row.ResolvedLocationName ?? row.LocationCode!;
            var nameKey = NormalizeKey(name);
            var existing = state.Locations.SingleOrDefault(
                location =>
                    location.CodeNormalized == codeKey ||
                    NormalizeKey(location.Name) == nameKey);
            if (existing is not null)
            {
                locationIdsByPlanCode.Add(codeKey, existing.Id);
                continue;
            }

            var location = Location.Create(code, name);
            _repository.AddLocation(location);
            locationIdsByPlanCode.Add(codeKey, location.Id);
            createdLocations.Add(location);
        }

        var createdCategoryIds = createdCategories.Select(category => category.Id).ToHashSet();
        var createdLocationIds = createdLocations.Select(location => location.Id).ToHashSet();
        var resolvedRows = rows
            .Select(
                row =>
                {
                    var categoryId = row.CategoryId ??
                                     categoryIdsByPlanCode[
                                         NormalizeKey(
                                             row.ResolvedCategoryCode ?? row.Category)];
                    Guid? locationId = row.LocationId;
                    if (!locationId.HasValue && row.LocationWillBeCreated)
                    {
                        locationId = locationIdsByPlanCode[
                            NormalizeKey(row.ResolvedLocationCode ?? row.LocationCode!)];
                    }

                    return row with
                    {
                        CategoryId = categoryId,
                        LocationId = locationId,
                        CategoryWillBeCreated = createdCategoryIds.Contains(categoryId),
                        LocationWillBeCreated =
                            locationId.HasValue && createdLocationIds.Contains(locationId.Value),
                    };
                })
            .ToArray();
        return new MaterializedImportReferences(
            resolvedRows,
            createdCategories,
            createdLocations);
    }

    private void ApplyAssetAndAssignment(
        ValidatedAssetImportRow row,
        Asset asset,
        AssetAssignment? assignment,
        IReadOnlyDictionary<Guid, Inventory.Domain.People.Person> people,
        IReadOnlyDictionary<Guid, Inventory.Domain.People.Department> departments,
        CurrentActor actor,
        DateTimeOffset now,
        HashSet<string> changedFields)
    {
        var wantsAssignment = row.Status == AssetStatus.Assigned;
        var sameTarget = assignment is not null &&
                         assignment.PersonId == row.PersonId &&
                         assignment.DepartmentId == row.DepartmentId;

        try
        {
            if (assignment is not null && !wantsAssignment)
            {
                asset.Return(asset.Version, now, row.Condition, row.LocationId);
                assignment.Close(
                    assignment.Version,
                    now,
                    actor.UserId,
                    actor.DisplayName,
                    row.Condition,
                    row.LocationId,
                    $"Closed by asset import {row.RowNumber}.");
                changedFields.Add("assignment");
            }
            else if (assignment is not null && wantsAssignment && !sameTarget)
            {
                asset.Transfer(asset.Version, now);
                assignment.Close(
                    assignment.Version,
                    now,
                    actor.UserId,
                    actor.DisplayName,
                    row.Condition,
                    row.LocationId,
                    $"Transferred by asset import row {row.RowNumber}.");
                AddAssignment(row, asset, people, departments, actor, now);
                changedFields.Add("assignment");
            }

            var updateStatus = wantsAssignment && asset.Status != AssetStatus.Assigned
                ? AssetStatus.InStock
                : row.Status;
            var changes = asset.Update(
                ToDetails(row, updateStatus, asset),
                asset.Version,
                now);
            foreach (var field in changes.Keys)
            {
                changedFields.Add(field);
            }

            if (wantsAssignment && assignment is null)
            {
                asset.Assign(asset.Version, now);
                AddAssignment(row, asset, people, departments, actor, now);
                changedFields.Add("assignment");
            }
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }
    }

    private void AddAssignment(
        ValidatedAssetImportRow row,
        Asset asset,
        IReadOnlyDictionary<Guid, Inventory.Domain.People.Person> people,
        IReadOnlyDictionary<Guid, Inventory.Domain.People.Department> departments,
        CurrentActor actor,
        DateTimeOffset now)
    {
        string displayName;
        string? reference;
        if (row.PersonId.HasValue)
        {
            var person = people[row.PersonId.Value];
            displayName = person.DisplayName;
            reference = person.ExternalId;
        }
        else
        {
            var department = departments[row.DepartmentId!.Value];
            displayName = department.Name;
            reference = department.Code;
        }

        _repository.AddAssignment(
            AssetAssignment.Create(
                asset.Id,
                row.PersonId,
                row.DepartmentId,
                displayName,
                reference,
                now,
                actor.UserId,
                actor.DisplayName,
                asset.Condition,
                asset.LocationId,
                $"Created by asset import row {row.RowNumber}."));
    }

    private void ApplyWarranty(
        ValidatedAssetImportRow row,
        Asset asset,
        WarrantyCoverage? warranty,
        DateTimeOffset now,
        HashSet<string> changedFields)
    {
        if (row.WarrantyProvider is null)
        {
            return;
        }

        try
        {
            if (warranty is null)
            {
                _repository.AddWarranty(
                    WarrantyCoverage.Create(
                        asset.Id,
                        row.WarrantyProvider,
                        ImportedWarrantyType,
                        row.WarrantyStartDate,
                        row.WarrantyEndDate,
                        row.WarrantyReference,
                        notes: null,
                        isPrimary: true,
                        now));
                changedFields.Add("warranty");
                return;
            }

            if (warranty.Provider == row.WarrantyProvider &&
                warranty.StartDate == row.WarrantyStartDate &&
                warranty.EndDate == row.WarrantyEndDate &&
                warranty.Reference == row.WarrantyReference &&
                warranty.IsPrimary)
            {
                return;
            }

            warranty.Update(
                row.WarrantyProvider,
                warranty.CoverageType,
                row.WarrantyStartDate,
                row.WarrantyEndDate,
                row.WarrantyReference,
                warranty.Notes,
                isPrimary: true,
                warranty.Version,
                now);
            changedFields.Add("warranty");
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }
    }

    private static Asset CreateAsset(
        ValidatedAssetImportRow row,
        DateTimeOffset now)
    {
        try
        {
            return Asset.Create(
                ToDetails(
                    row,
                    row.Status == AssetStatus.Assigned
                        ? AssetStatus.InStock
                        : row.Status,
                    existingAsset: null),
                now);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }
    }

    private static AssetDetails ToDetails(
        ValidatedAssetImportRow row,
        AssetStatus status,
        Asset? existingAsset) =>
        new(
            row.AssetTag,
            row.CategoryId!.Value,
            row.Manufacturer,
            row.Model,
            row.SerialNumber,
            status,
            row.Condition,
            row.LocationId,
            row.PurchaseDate,
            row.PurchaseCost,
            row.Currency,
            row.Supplier,
            row.Notes,
            row.LegacyId,
            row.PreserveVersion2Fields ? existingAsset?.DeviceName : row.DeviceName,
            row.PreserveVersion2Fields ? existingAsset?.Description : row.Description,
            row.PreserveVersion2Fields ? existingAsset?.LanMacAddress : row.LanMacAddress,
            row.PreserveVersion2Fields ? existingAsset?.BiosGuid : row.BiosGuid,
            row.PreserveVersion2Fields ? existingAsset?.Ownership : row.Ownership,
            row.PreserveVersion2Fields ? existingAsset?.OperatingSystem : row.OperatingSystem,
            row.PreserveVersion2Fields
                ? existingAsset?.OperatingSystemVersion
                : row.OperatingSystemVersion,
            row.PreserveVersion2Fields
                ? existingAsset?.Windows11Compliant
                : row.Windows11Compliant);

    private AssetImportJobView ToView(AssetImportJob job)
    {
        var rows = DeserializeRows(job.ValidatedRowsJson);
        var errors = DeserializeErrors(job.ErrorsJson);
        return new AssetImportJobView(
            job.Id,
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
            rows.Count(row => row.AssetTagGenerated),
            rows.Where(row => row.CategoryWillBeCreated)
                .Select(row => row.ResolvedCategoryCode)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            rows.Where(row => row.LocationWillBeCreated)
                .Select(row => row.ResolvedLocationCode)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            rows.Count(row => (row.InferredDateFields?.Count ?? 0) > 0),
            rows.Count(
                row =>
                    row.AssignmentResolution ==
                    AssetImportAssignmentResolution.FuzzyPerson),
            rows.Count(
                row =>
                    row.AssignmentResolution ==
                    AssetImportAssignmentResolution.Unassigned),
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
                    row => new AssetImportPreviewRow(
                        row.RowNumber,
                        row.Action,
                        row.AssetTag,
                        row.AssetTagGenerated,
                        row.Category,
                        row.ResolvedCategoryCode ?? row.Category,
                        row.CategoryWillBeCreated,
                        row.Status,
                        row.Condition,
                        row.LocationCode,
                        row.ResolvedLocationCode ?? row.LocationCode,
                        row.LocationWillBeCreated,
                        row.AssignedPersonExternalId ?? row.DepartmentCode,
                        row.AssignmentResolution,
                        row.AssignedToDisplayName,
                        row.PurchaseDate,
                        row.WarrantyStartDate,
                        row.WarrantyEndDate,
                        row.InferredDateFields ?? []))
                .ToArray(),
            errors.Take(_settings.PreviewErrors).ToArray(),
            rows.Count > _settings.PreviewRows,
            errors.Count > _settings.PreviewErrors,
            job.Version);
    }

    private static IReadOnlyList<string?> ToCsvFields(AssetExportRow row) =>
    [
        ImportExportSettings.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture),
        row.AssetTag,
        row.DeviceName,
        row.CategoryCode,
        row.Description,
        row.Manufacturer,
        row.Model,
        row.SerialNumber,
        row.LanMacAddress,
        row.BiosGuid?.ToString("D"),
        row.Ownership,
        row.Status.ToString(),
        row.Condition.ToString(),
        row.LocationCode,
        row.DepartmentCode,
        row.AssignedPersonExternalId,
        row.PurchaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        row.PurchaseCost?.ToString("0.00", CultureInfo.InvariantCulture),
        row.Currency,
        row.Supplier,
        row.WarrantyStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        row.WarrantyEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        row.WarrantyProvider,
        row.WarrantyReference,
        row.OperatingSystem,
        row.OperatingSystemVersion,
        row.Windows11Compliant?.ToString().ToLowerInvariant(),
        row.LegacyId,
        row.Notes,
    ];

    private static AssetExportFilter NormalizeExportFilter(AssetExportFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.CreatedFrom.HasValue &&
            filter.CreatedTo.HasValue &&
            filter.CreatedTo.Value < filter.CreatedFrom.Value)
        {
            throw new RequestValidationException(
                "invalid_created_range",
                "createdTo cannot precede createdFrom.");
        }

        return filter with
        {
            Query = NormalizeSearchValue(filter.Query, "q"),
            AssetTag = NormalizeSearchValue(filter.AssetTag, "assetTag"),
            SerialNumber = NormalizeSearchValue(filter.SerialNumber, "serialNumber"),
            LegacyId = NormalizeSearchValue(filter.LegacyId, "legacyId"),
            Sort = filter.Sort switch
            {
                "assetTag" or "-assetTag" or "updatedAtUtc" or "-updatedAtUtc" or
                    "status" or "-status" => filter.Sort,
                _ => "assetTag",
            },
        };
    }

    private static string? NormalizeSearchValue(string? value, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > 200)
        {
            throw new RequestValidationException(
                "invalid_filter",
                $"{field} cannot exceed 200 characters.");
        }

        return normalized;
    }

    private string ValidateFileMetadata(AssetImportFile file)
    {
        if (string.IsNullOrWhiteSpace(file.FileName) ||
            !string.Equals(
                Path.GetExtension(file.FileName),
                ".csv",
                StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidFile("A file with the .csv extension is required.");
        }

        var fileName = Path.GetFileName(file.FileName.Trim());
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 260 ||
            fileName.Any(char.IsControl))
        {
            throw InvalidFile(
                "The CSV filename is empty, unsafe, or longer than 260 characters.");
        }

        var mediaType = file.ContentType.Split(';', 2)[0].Trim();
        if (!AllowedContentTypes.Contains(mediaType))
        {
            throw InvalidFile(
                "The upload content type must identify a CSV or binary file.");
        }

        if (file.Length <= 0)
        {
            throw InvalidFile("The CSV file is empty.");
        }

        if (file.Length > _settings.MaxImportBytes)
        {
            throw InvalidFile(
                $"The CSV exceeds the {_settings.MaxImportBytes} byte upload limit.");
        }

        if (file.Content is null || !file.Content.CanRead)
        {
            throw InvalidFile("The uploaded CSV cannot be read.");
        }

        return fileName;
    }

    private async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > _settings.MaxImportBytes)
            {
                throw InvalidFile(
                    $"The CSV exceeds the {_settings.MaxImportBytes} byte upload limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    private static CsvInputFormat? DetectInputFormat(IReadOnlyList<string> header)
    {
        if (HeaderMatches(header, AssetCsvSchema.Headers))
        {
            return CsvInputFormat.CanonicalVersion2;
        }

        if (HeaderMatches(header, AssetCsvSchema.Version1Headers))
        {
            return CsvInputFormat.CanonicalVersion1;
        }

        return HeaderMatches(header, AssetCsvSchema.StrLegacyHeaders)
            ? CsvInputFormat.StrLegacyRegister
            : null;
    }

    private static IReadOnlyList<string> HeadersFor(CsvInputFormat inputFormat) =>
        inputFormat switch
        {
            CsvInputFormat.CanonicalVersion1 => AssetCsvSchema.Version1Headers,
            CsvInputFormat.CanonicalVersion2 => AssetCsvSchema.Headers,
            CsvInputFormat.StrLegacyRegister => AssetCsvSchema.StrLegacyHeaders,
            _ => throw new ArgumentOutOfRangeException(nameof(inputFormat), inputFormat, null),
        };

    private static int SchemaVersionFor(CsvInputFormat inputFormat) =>
        inputFormat == CsvInputFormat.CanonicalVersion1
            ? 1
            : ImportExportSettings.SupportedSchemaVersion;

    private static bool HeaderMatches(
        IReadOnlyList<string> actual,
        IReadOnlyList<string> expected) =>
        actual.Count == expected.Count &&
        actual.SequenceEqual(expected, StringComparer.Ordinal);

    private static ImportReferenceResolution? ResolveCategory(
        CandidateRow row,
        Dictionary<string, ImportReferenceLookup[]> byCode,
        Dictionary<string, ImportReferenceLookup[]> byName,
        Dictionary<string, ImportReferenceResolution> plannedBySource,
        HashSet<string> reservedCodes,
        List<AssetImportError> errors)
    {
        if (row.Category is null)
        {
            return null;
        }

        var key = NormalizeKey(row.Category);
        byCode.TryGetValue(key, out var codeMatches);
        byName.TryGetValue(key, out var nameMatches);
        var matches = (codeMatches ?? [])
            .Concat(nameMatches ?? [])
            .DistinctBy(reference => reference.Id)
            .ToArray();
        if (matches.Length == 0)
        {
            if (!plannedBySource.TryGetValue(key, out var planned))
            {
                planned = new ImportReferenceResolution(
                    Id: null,
                    Version: null,
                    GenerateUniqueReferenceCode(row.Category, "CATEGORY", reservedCodes),
                    row.Category,
                    WillBeCreated: true);
                plannedBySource.Add(key, planned);
            }

            return planned;
        }

        if (matches.Length > 1)
        {
            AddError(
                errors,
                row.RowNumber,
                "category",
                "ambiguous_category",
                "The category value matches more than one category; use its unique code.",
                row.Category);
            return null;
        }

        if (!matches[0].IsActive)
        {
            AddError(
                errors,
                row.RowNumber,
                "category",
                "inactive_category",
                "The category is inactive.",
                row.Category);
            return null;
        }

        var match = matches[0];
        return new ImportReferenceResolution(
            match.Id,
            match.Version,
            match.Code,
            match.Name,
            WillBeCreated: false);
    }

    private static ImportReferenceResolution? ResolveLocation(
        CandidateRow row,
        Dictionary<string, ImportReferenceLookup[]> byCode,
        Dictionary<string, ImportReferenceLookup[]> byName,
        Dictionary<string, ImportReferenceResolution> plannedBySource,
        HashSet<string> reservedCodes,
        List<AssetImportError> errors)
    {
        if (row.LocationCode is null)
        {
            return null;
        }

        var key = NormalizeKey(row.LocationCode);
        byCode.TryGetValue(key, out var codeMatches);
        byName.TryGetValue(key, out var nameMatches);
        var matches = (codeMatches ?? [])
            .Concat(nameMatches ?? [])
            .DistinctBy(reference => reference.Id)
            .ToArray();
        if (matches.Length == 0)
        {
            if (!plannedBySource.TryGetValue(key, out var planned))
            {
                planned = new ImportReferenceResolution(
                    Id: null,
                    Version: null,
                    GenerateUniqueReferenceCode(row.LocationCode, "LOCATION", reservedCodes),
                    row.LocationCode,
                    WillBeCreated: true);
                plannedBySource.Add(key, planned);
            }

            return planned;
        }

        if (matches.Length > 1)
        {
            AddError(
                errors,
                row.RowNumber,
                "locationCode",
                "ambiguous_location",
                "The location value matches more than one location; use its unique code.",
                row.LocationCode);
            return null;
        }

        if (!matches[0].IsActive)
        {
            AddError(
                errors,
                row.RowNumber,
                "locationCode",
                "inactive_location",
                "The location is inactive.",
                row.LocationCode);
            return null;
        }

        var match = matches[0];
        return new ImportReferenceResolution(
            match.Id,
            match.Version,
            match.Code,
            match.Name,
            WillBeCreated: false);
    }

    private static ImportReferenceLookup? ResolveDepartment(
        CandidateRow row,
        Dictionary<string, ImportReferenceLookup[]> byCode)
    {
        if (row.DepartmentCode is null)
        {
            return null;
        }

        if (!byCode.TryGetValue(
                NormalizeKey(row.DepartmentCode),
                out var matches) ||
            matches.Length != 1 ||
            !matches[0].IsActive)
        {
            return null;
        }

        return matches[0];
    }

    private static PersonImportResolution ResolvePerson(
        CandidateRow row,
        Dictionary<string, ImportPersonLookup[]> byExternalId,
        IReadOnlyList<ImportPersonLookup> people)
    {
        if (row.AssignedPersonExternalId is null)
        {
            return new PersonImportResolution(
                Person: null,
                AssetImportAssignmentResolution.None);
        }

        var source = NormalizeKey(row.AssignedPersonExternalId);
        if (byExternalId.TryGetValue(source, out var exactMatches))
        {
            return exactMatches.Length == 1 && exactMatches[0].IsActive
                ? new PersonImportResolution(
                    exactMatches[0],
                    AssetImportAssignmentResolution.ExactPerson)
                : new PersonImportResolution(
                    Person: null,
                    AssetImportAssignmentResolution.Unassigned);
        }

        var fuzzyMatches = people
            .Where(
                person =>
                    person.IsActive &&
                    IsSafeFuzzyExternalIdMatch(source, NormalizeKey(person.ExternalId)))
            .DistinctBy(person => person.Id)
            .Take(2)
            .ToArray();
        return fuzzyMatches.Length == 1
            ? new PersonImportResolution(
                fuzzyMatches[0],
                AssetImportAssignmentResolution.FuzzyPerson)
            : new PersonImportResolution(
                Person: null,
                AssetImportAssignmentResolution.Unassigned);
    }

    private static bool IsSafeFuzzyExternalIdMatch(string source, string candidate)
    {
        const int minimumLength = 6;
        const int stablePrefixLength = 2;

        if (source.Length < minimumLength ||
            candidate.Length < minimumLength ||
            source.Length == candidate.Length && source == candidate ||
            Math.Abs(source.Length - candidate.Length) > 1 ||
            !source.AsSpan(0, stablePrefixLength)
                .SequenceEqual(candidate.AsSpan(0, stablePrefixLength)))
        {
            return false;
        }

        return IsSingleEditOrAdjacentTransposition(source, candidate);
    }

    private static bool IsSingleEditOrAdjacentTransposition(
        string source,
        string candidate)
    {
        if (source.Length == candidate.Length)
        {
            Span<int> differences = stackalloc int[2];
            var differenceCount = 0;
            for (var index = 0; index < source.Length; index++)
            {
                if (source[index] == candidate[index])
                {
                    continue;
                }

                if (differenceCount == differences.Length)
                {
                    return false;
                }

                differences[differenceCount++] = index;
            }

            if (differenceCount == 1)
            {
                return true;
            }

            return differenceCount == 2 &&
                   differences[1] == differences[0] + 1 &&
                   source[differences[0]] == candidate[differences[1]] &&
                   source[differences[1]] == candidate[differences[0]];
        }

        var shorter = source.Length < candidate.Length ? source : candidate;
        var longer = source.Length < candidate.Length ? candidate : source;
        var shorterIndex = 0;
        var longerIndex = 0;
        var skippedCharacter = false;
        while (shorterIndex < shorter.Length && longerIndex < longer.Length)
        {
            if (shorter[shorterIndex] == longer[longerIndex])
            {
                shorterIndex++;
                longerIndex++;
                continue;
            }

            if (skippedCharacter)
            {
                return false;
            }

            skippedCharacter = true;
            longerIndex++;
        }

        return true;
    }

    private static TEnum ParseEnum<TEnum>(
        string value,
        TEnum defaultValue,
        int rowNumber,
        string column,
        List<AssetImportError> errors)
        where TEnum : struct, Enum
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return defaultValue;
        }

        if (!int.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _) &&
            Enum.TryParse<TEnum>(normalized, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        AddError(
            errors,
            rowNumber,
            column,
            $"invalid_{column}",
            $"{column} is not a supported value.",
            value);
        return defaultValue;
    }

    private static string? ParseMacAddress(
        string value,
        int rowNumber,
        List<AssetImportError> errors)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        var compact = normalized
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (compact.Length == 12 &&
            compact.All(char.IsAsciiHexDigit))
        {
            return string.Join(
                ":",
                Enumerable.Range(0, 6)
                    .Select(index => compact.Substring(index * 2, 2).ToUpperInvariant()));
        }

        AddError(
            errors,
            rowNumber,
            "lanMacAddress",
            "invalid_lan_mac_address",
            "lanMacAddress must contain six hexadecimal octets.",
            value);
        return null;
    }

    private static Guid? ParseGuid(
        string value,
        int rowNumber,
        string column,
        List<AssetImportError> errors)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        if (Guid.TryParse(normalized, out var parsed))
        {
            return parsed;
        }

        AddError(
            errors,
            rowNumber,
            column,
            $"invalid_{column}",
            $"{column} must be a valid GUID.",
            value);
        return null;
    }

    private static bool? ParseNullableBoolean(
        string value,
        int rowNumber,
        string column,
        List<AssetImportError> errors)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        AddError(
            errors,
            rowNumber,
            column,
            $"invalid_{column}",
            $"{column} must be true, false, yes, no, or blank.",
            value);
        return null;
    }

    private static DateOnly? ParseDate(
        string value,
        int rowNumber,
        string column,
        List<AssetImportError> errors,
        bool allowLegacyDateFormats,
        PartialDateBoundary partialDateBoundary,
        List<string> inferredDateFields)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                normalized,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        if (allowLegacyDateFormats &&
            DateOnly.TryParseExact(
                normalized,
                ["dd/MM/yyyy", "dd.MM.yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed))
        {
            return parsed;
        }

        if (TryParsePartialDate(normalized, partialDateBoundary, out parsed))
        {
            inferredDateFields.Add(column);
            return parsed;
        }

        if (allowLegacyDateFormats &&
            TryParseExcelSerialDate(normalized, out parsed))
        {
            inferredDateFields.Add(column);
            return parsed;
        }

        AddError(
            errors,
            rowNumber,
            column,
            $"invalid_{column}",
            allowLegacyDateFormats
                ? $"{column} must use yyyy-MM-dd, dd/MM/yyyy, dd.MM.yyyy, yyyy, " +
                  "MM/yyyy, MM.yyyy, or a valid Excel serial date."
                : $"{column} must use yyyy-MM-dd, yyyy, MM/yyyy, or MM.yyyy.",
            value);
        return null;
    }

    private static bool TryParsePartialDate(
        string value,
        PartialDateBoundary boundary,
        out DateOnly parsed)
    {
        parsed = default;
        if (value.Length == 4 &&
            int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var year) &&
            year is >= 1 and <= 9999)
        {
            parsed = boundary == PartialDateBoundary.Start
                ? new DateOnly(year, 1, 1)
                : new DateOnly(year, 12, 31);
            return true;
        }

        if (value.Length != 7 || value[2] is not ('.' or '/'))
        {
            return false;
        }

        if (!int.TryParse(
                value.AsSpan(0, 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var month) ||
            !int.TryParse(
                value.AsSpan(3, 4),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out year) ||
            month is < 1 or > 12 ||
            year is < 1 or > 9999)
        {
            return false;
        }

        parsed = boundary == PartialDateBoundary.Start
            ? new DateOnly(year, month, 1)
            : new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return true;
    }

    private static bool TryParseExcelSerialDate(string value, out DateOnly parsed)
    {
        parsed = default;
        if (value.Length < 5 ||
            !int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var serial))
        {
            return false;
        }

        try
        {
            var dateTime = DateTime.FromOADate(serial);
            if (dateTime.Year < 1900)
            {
                return false;
            }

            parsed = DateOnly.FromDateTime(dateTime);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static decimal? ParseDecimal(
        string value,
        int rowNumber,
        string column,
        List<AssetImportError> errors)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        if (decimal.TryParse(
                normalized,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed) &&
            decimal.Round(parsed, 2) == parsed)
        {
            return parsed;
        }

        AddError(
            errors,
            rowNumber,
            column,
            $"invalid_{column}",
            $"{column} must be an invariant decimal with at most two decimal places.",
            value);
        return null;
    }

    private static string? Required(
        string value,
        int maximumLength,
        int rowNumber,
        string column,
        List<AssetImportError> errors)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            AddError(
                errors,
                rowNumber,
                column,
                "required",
                $"{column} is required.",
                value);
            return null;
        }

        return ValidateLength(normalized, maximumLength, rowNumber, column, errors);
    }

    private static string? Optional(
        string value,
        int maximumLength,
        int rowNumber,
        string column,
        List<AssetImportError> errors)
    {
        var normalized = value.Trim();
        return normalized.Length == 0
            ? null
            : ValidateLength(normalized, maximumLength, rowNumber, column, errors);
    }

    private static string? ValidateLength(
        string value,
        int maximumLength,
        int rowNumber,
        string column,
        List<AssetImportError> errors)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        AddError(
            errors,
            rowNumber,
            column,
            "maximum_length",
            $"{column} cannot exceed {maximumLength} characters.",
            value);
        return null;
    }

    private static void AddError(
        List<AssetImportError> errors,
        int rowNumber,
        string column,
        string code,
        string message,
        string? value)
    {
        if (errors.Count(error => error.RowNumber == rowNumber) >= 10)
        {
            return;
        }

        errors.Add(
            new AssetImportError(
                rowNumber,
                column,
                code,
                message,
                TruncateErrorValue(value)));
    }

    private static AssetImportError FileError(string code, string message) =>
        new(0, "file", code, message, null);

    private static string? TruncateErrorValue(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= 200 ? value : $"{value[..197]}...";
    }

    private static RequestValidationException InvalidFile(string message) =>
        new(
            "invalid_import_file",
            message,
            new Dictionary<string, string[]>
            {
                ["file"] = [message],
            });

    private string GenerateAssetTag(string fileHash, int rowNumber) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{_settings.GeneratedAssetTagPrefix.Trim().ToUpperInvariant()}-" +
            $"{fileHash[..16]}-{rowNumber:D4}");

    private static string GenerateUniqueReferenceCode(
        string name,
        string fallback,
        HashSet<string> reservedCodes)
    {
        var baseCode = GenerateReferenceCode(name);
        if (baseCode.Length == 0)
        {
            baseCode = fallback;
        }

        if (reservedCodes.Add(NormalizeKey(baseCode)))
        {
            return baseCode;
        }

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeKey(name))))[..8];
        var suffix = $"-{hash}";
        var candidate = $"{baseCode[..Math.Min(baseCode.Length, 50 - suffix.Length)]}{suffix}";
        if (reservedCodes.Add(NormalizeKey(candidate)))
        {
            return candidate;
        }

        for (var sequence = 2; ; sequence++)
        {
            suffix = $"-{hash}-{sequence.ToString(CultureInfo.InvariantCulture)}";
            candidate =
                $"{baseCode[..Math.Min(baseCode.Length, 50 - suffix.Length)]}{suffix}";
            if (reservedCodes.Add(NormalizeKey(candidate)))
            {
                return candidate;
            }
        }
    }

    private static string GenerateReferenceCode(string name)
    {
        var builder = new StringBuilder();
        var separatorPending = false;
        foreach (var character in name.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (separatorPending && builder.Length > 0 && builder.Length < 50)
                {
                    builder.Append('-');
                }

                separatorPending = false;
                if (builder.Length < 50)
                {
                    builder.Append(character);
                }
            }
            else
            {
                separatorPending = true;
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    private static string NormalizeKey(string value) =>
        value.Trim().ToUpper(CultureInfo.InvariantCulture);

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content));

    private static void ThrowStale(int rowNumber, string resource) =>
        throw new ResourceConflictException(
            "import_snapshot_stale",
            $"Import row {rowNumber} references a {resource} that changed after validation. " +
            "Validate the file again.");

    private static void ThrowIdentifierConflict(int rowNumber, string column) =>
        throw new ResourceConflictException(
            "import_identifier_conflict",
            $"Import row {rowNumber} now conflicts on {column}. Validate the file again.");

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
        AssetImportJob job,
        CurrentActor actor,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        _repository.AddAuditLog(
            CreateAudit(
                actor,
                "assets.import.commit_rejected",
                "asset_import_job",
                job.Id.ToString(),
                "rejected",
                new
                {
                    code,
                    job.FileHash,
                    job.Mode,
                    job.Status,
                }));
        await SaveChangesAsync(cancellationToken);
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

    private static List<ValidatedAssetImportRow> DeserializeRows(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ValidatedAssetImportRow>>(
                       json,
                       SnapshotJsonOptions)
                   ?? throw new JsonException("The row snapshot was empty.");
        }
        catch (JsonException exception)
        {
            throw new ResourceConflictException(
                "import_snapshot_invalid",
                $"The validated import snapshot cannot be read: {exception.Message}");
        }
    }

    private static List<AssetImportError> DeserializeErrors(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<AssetImportError>>(
                       json,
                       SnapshotJsonOptions)
                   ?? throw new JsonException("The error snapshot was empty.");
        }
        catch (JsonException exception)
        {
            throw new ResourceConflictException(
                "import_snapshot_invalid",
                $"The import error snapshot cannot be read: {exception.Message}");
        }
    }

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
        CanonicalVersion1,
        CanonicalVersion2,
        StrLegacyRegister,
    }

    private enum PartialDateBoundary
    {
        Start,
        End,
    }

    private sealed record CsvSourceRecord(
        int RowNumber,
        IReadOnlyList<string> Values);

    private sealed record ImportReferenceResolution(
        Guid? Id,
        Guid? Version,
        string Code,
        string Name,
        bool WillBeCreated);

    private sealed record PersonImportResolution(
        ImportPersonLookup? Person,
        AssetImportAssignmentResolution Resolution);

    private sealed record MaterializedImportReferences(
        IReadOnlyList<ValidatedAssetImportRow> Rows,
        IReadOnlyList<AssetCategory> CreatedCategories,
        IReadOnlyList<Location> CreatedLocations);

    private sealed record CandidateRow(
        int RowNumber,
        bool PreserveVersion2Fields,
        bool AssetTagGenerated,
        string? AssetTag,
        string? AssetTagNormalized,
        string? DeviceName,
        string? Category,
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
        IReadOnlyList<string> InferredDateFields);
}
