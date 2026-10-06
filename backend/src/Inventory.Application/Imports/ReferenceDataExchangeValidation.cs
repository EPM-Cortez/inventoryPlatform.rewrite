using System.Net.Mail;
using Inventory.Application.Common;
using Inventory.Domain.Importing;
using Inventory.Domain.Locations;
using Inventory.Domain.People;

namespace Inventory.Application.DataExchange;

public sealed partial class ReferenceDataExchangeService
{
    private async Task<ReferenceDataImportJobView> ValidatePeopleAsync(
        ReferenceDataImportFile file,
        ReferenceDataImportMode mode,
        CancellationToken cancellationToken)
    {
        EnsureMode(mode);
        var actor = _currentUser.RequireActor();
        var prepared = await PrepareFileAsync(file, cancellationToken);
        var parsed = ParseRows(
            prepared,
            ReferenceDataCsvSchemas.PeopleHeaders,
            ReferenceDataCsvSchemas.StaffCompatibilityHeaders);
        if (parsed.FileError is not null)
        {
            return await PersistValidationAsync(
                ReferenceDataImportResource.People,
                prepared.FileName,
                prepared.FileHash,
                mode,
                Array.Empty<ValidatedPersonImportRow>(),
                [parsed.FileError],
                0,
                actor,
                cancellationToken);
        }

        var errors = new List<ReferenceDataImportError>();
        var candidates = new List<PersonImportCandidate>();
        foreach (var record in parsed.Records)
        {
            if (!HasExpectedFieldCount(
                    record,
                    parsed.Format == CsvInputFormat.Canonical
                        ? ReferenceDataCsvSchemas.PeopleHeaders.Count
                        : ReferenceDataCsvSchemas.StaffCompatibilityHeaders.Count,
                    errors))
            {
                continue;
            }

            var before = errors.Count;
            var canonical = parsed.Format == CsvInputFormat.Canonical;
            if (canonical)
            {
                ValidateSchemaVersion(record, errors);
            }

            var externalId = canonical ? Field(record, 1) : string.Empty;
            var displayName = Field(record, canonical ? 2 : 0);
            var personTypeText = Field(record, canonical ? 3 : 1);
            var email = EmptyToNull(Field(record, canonical ? 4 : 2));
            var isActiveText = canonical ? Field(record, 5) : "true";

            ValidateRequired(
                displayName,
                "displayName",
                200,
                record.RowNumber,
                errors);
            var personType = ParsePersonType(
                personTypeText,
                record.RowNumber,
                errors);
            ValidateOptional(email, "email", 320, record.RowNumber, errors);
            if (email is not null && !MailAddress.TryCreate(email, out _))
            {
                AddError(
                    errors,
                    record.RowNumber,
                    "email",
                    "invalid_email",
                    "Email must be a valid email address.",
                    email);
            }

            var keyGenerated = string.IsNullOrWhiteSpace(externalId);
            if (keyGenerated && email is not null)
            {
                externalId = EmailLocalPart(email);
            }

            ValidateRequired(
                externalId,
                "externalId",
                100,
                record.RowNumber,
                errors,
                "External ID is required when an email local-part cannot be derived.");
            var isActive = ParseBoolean(
                isActiveText,
                "isActive",
                record.RowNumber,
                errors);

            if (errors.Count != before ||
                personType is null ||
                isActive is null)
            {
                continue;
            }

            candidates.Add(
                new PersonImportCandidate(
                    record.RowNumber,
                    keyGenerated,
                    externalId,
                    NormalizeKey(externalId),
                    displayName,
                    personType.Value,
                    email,
                    email is null ? null : NormalizeKey(email),
                    isActive.Value));
        }

        MarkDuplicatePeople(candidates, errors);
        var usable = candidates
            .Where(candidate => !HasRowError(errors, candidate.RowNumber))
            .ToArray();
        var existing = await _repository.GetPeopleAsync(
            [],
            usable.Select(candidate => candidate.ExternalIdNormalized).Distinct().ToArray(),
            usable
                .Where(candidate => candidate.EmailNormalized is not null)
                .Select(candidate => candidate.EmailNormalized!)
                .Distinct()
                .ToArray(),
            forUpdate: false,
            cancellationToken);
        var existingByKey = existing
            .Where(person => person.ExternalIdNormalized is not null)
            .GroupBy(person => person.ExternalIdNormalized!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var existingByEmail = existing
            .Where(person => person.Email is not null)
            .GroupBy(person => NormalizeKey(person.Email!), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Person>)group.ToArray(),
                StringComparer.Ordinal);

        var preliminary = new List<ValidatedPersonImportRow>();
        foreach (var candidate in usable)
        {
            existingByKey.TryGetValue(candidate.ExternalIdNormalized, out var matched);
            if (mode == ReferenceDataImportMode.CreateOnly && matched is not null)
            {
                AddError(
                    errors,
                    candidate.RowNumber,
                    "externalId",
                    "person_already_exists",
                    "A person with this external ID already exists.",
                    candidate.ExternalId);
            }

            if (candidate.EmailNormalized is not null &&
                existingByEmail.TryGetValue(
                    candidate.EmailNormalized,
                    out var emailMatches) &&
                emailMatches.Any(person => person.Id != matched?.Id))
            {
                AddError(
                    errors,
                    candidate.RowNumber,
                    "email",
                    "duplicate_person_email",
                    "This email belongs to a different person.",
                    candidate.Email);
            }

            if (HasRowError(errors, candidate.RowNumber))
            {
                continue;
            }

            preliminary.Add(
                new ValidatedPersonImportRow(
                    candidate.RowNumber,
                    matched is null
                        ? ReferenceDataImportAction.Create
                        : ReferenceDataImportAction.Update,
                    candidate.KeyGenerated,
                    matched?.Id,
                    matched?.Version,
                    candidate.ExternalId,
                    candidate.ExternalIdNormalized,
                    candidate.DisplayName,
                    candidate.PersonType,
                    candidate.Email,
                    candidate.EmailNormalized,
                    candidate.IsActive));
        }

        var deactivatedIds = preliminary
            .Where(row => !row.IsActive && row.PersonId.HasValue)
            .Select(row => row.PersonId!.Value)
            .ToArray();
        var holders = (await _repository.GetPeopleWithOpenAssignmentsAsync(
                deactivatedIds,
                cancellationToken))
            .ToHashSet();
        foreach (var row in preliminary.Where(
                     row => row.PersonId.HasValue && holders.Contains(row.PersonId.Value)))
        {
            AddError(
                errors,
                row.RowNumber,
                "isActive",
                "holder_has_active_assignments",
                "A person with currently assigned assets cannot be made inactive.",
                "false");
        }

        var rows = preliminary
            .Where(row => !HasRowError(errors, row.RowNumber))
            .ToArray();
        return await PersistValidationAsync(
            ReferenceDataImportResource.People,
            prepared.FileName,
            prepared.FileHash,
            mode,
            rows,
            errors,
            parsed.Records.Count,
            actor,
            cancellationToken);
    }

    private async Task<ReferenceDataImportJobView> ValidateDepartmentsAsync(
        ReferenceDataImportFile file,
        ReferenceDataImportMode mode,
        CancellationToken cancellationToken)
    {
        EnsureMode(mode);
        var actor = _currentUser.RequireActor();
        var prepared = await PrepareFileAsync(file, cancellationToken);
        var parsed = ParseRows(
            prepared,
            ReferenceDataCsvSchemas.DepartmentHeaders,
            ReferenceDataCsvSchemas.DepartmentCompatibilityHeaders);
        if (parsed.FileError is not null)
        {
            return await PersistValidationAsync(
                ReferenceDataImportResource.Departments,
                prepared.FileName,
                prepared.FileHash,
                mode,
                Array.Empty<ValidatedDepartmentImportRow>(),
                [parsed.FileError],
                0,
                actor,
                cancellationToken);
        }

        var errors = new List<ReferenceDataImportError>();
        var candidates = new List<DepartmentImportCandidate>();
        foreach (var record in parsed.Records)
        {
            if (!HasExpectedFieldCount(
                    record,
                    parsed.Format == CsvInputFormat.Canonical
                        ? ReferenceDataCsvSchemas.DepartmentHeaders.Count
                        : ReferenceDataCsvSchemas.DepartmentCompatibilityHeaders.Count,
                    errors))
            {
                continue;
            }

            var before = errors.Count;
            var canonical = parsed.Format == CsvInputFormat.Canonical;
            if (canonical)
            {
                ValidateSchemaVersion(record, errors);
            }

            var code = Field(record, canonical ? 1 : 1);
            var name = Field(record, canonical ? 2 : 0);
            var isActiveText = canonical ? Field(record, 3) : "true";
            ValidateRequired(name, "name", 150, record.RowNumber, errors);
            var keyGenerated = string.IsNullOrWhiteSpace(code);
            if (keyGenerated && !string.IsNullOrWhiteSpace(name))
            {
                code = GenerateCode(name);
            }

            ValidateRequired(
                code,
                "code",
                50,
                record.RowNumber,
                errors,
                "Code is required and could not be generated from the name.");
            var isActive = ParseBoolean(
                isActiveText,
                "isActive",
                record.RowNumber,
                errors);
            if (errors.Count != before || isActive is null)
            {
                continue;
            }

            candidates.Add(
                new DepartmentImportCandidate(
                    record.RowNumber,
                    keyGenerated,
                    code,
                    NormalizeKey(code),
                    name,
                    isActive.Value));
        }

        MarkDuplicateKeys(
            candidates.Select(
                candidate => new ImportKey(
                    candidate.RowNumber,
                    candidate.Code,
                    candidate.CodeNormalized)),
            "code",
            "duplicate_department_code_in_file",
            "Department code appears more than once in the file.",
            errors);
        var usable = candidates
            .Where(candidate => !HasRowError(errors, candidate.RowNumber))
            .ToArray();
        var existing = await _repository.GetDepartmentsAsync(
            [],
            usable.Select(candidate => candidate.CodeNormalized).Distinct().ToArray(),
            forUpdate: false,
            cancellationToken);
        var existingByCode = existing.ToDictionary(
            department => department.CodeNormalized,
            StringComparer.Ordinal);

        var preliminary = new List<ValidatedDepartmentImportRow>();
        foreach (var candidate in usable)
        {
            existingByCode.TryGetValue(candidate.CodeNormalized, out var matched);
            if (mode == ReferenceDataImportMode.CreateOnly && matched is not null)
            {
                AddError(
                    errors,
                    candidate.RowNumber,
                    "code",
                    "department_already_exists",
                    "A department with this code already exists.",
                    candidate.Code);
                continue;
            }

            preliminary.Add(
                new ValidatedDepartmentImportRow(
                    candidate.RowNumber,
                    matched is null
                        ? ReferenceDataImportAction.Create
                        : ReferenceDataImportAction.Update,
                    candidate.KeyGenerated,
                    matched?.Id,
                    matched?.Version,
                    candidate.Code,
                    candidate.CodeNormalized,
                    candidate.Name,
                    candidate.IsActive));
        }

        var deactivatedIds = preliminary
            .Where(row => !row.IsActive && row.DepartmentId.HasValue)
            .Select(row => row.DepartmentId!.Value)
            .ToArray();
        var holders = (await _repository.GetDepartmentsWithOpenAssignmentsAsync(
                deactivatedIds,
                cancellationToken))
            .ToHashSet();
        foreach (var row in preliminary.Where(
                     row =>
                         row.DepartmentId.HasValue &&
                         holders.Contains(row.DepartmentId.Value)))
        {
            AddError(
                errors,
                row.RowNumber,
                "isActive",
                "holder_has_active_assignments",
                "A department with currently assigned assets cannot be made inactive.",
                "false");
        }

        var rows = preliminary
            .Where(row => !HasRowError(errors, row.RowNumber))
            .ToArray();
        return await PersistValidationAsync(
            ReferenceDataImportResource.Departments,
            prepared.FileName,
            prepared.FileHash,
            mode,
            rows,
            errors,
            parsed.Records.Count,
            actor,
            cancellationToken);
    }

    private async Task<ReferenceDataImportJobView> ValidateLocationsAsync(
        ReferenceDataImportFile file,
        ReferenceDataImportMode mode,
        CancellationToken cancellationToken)
    {
        EnsureMode(mode);
        var actor = _currentUser.RequireActor();
        var prepared = await PrepareFileAsync(file, cancellationToken);
        var parsed = ParseRows(
            prepared,
            ReferenceDataCsvSchemas.LocationHeaders,
            ReferenceDataCsvSchemas.LocationCompatibilityHeaders);
        if (parsed.FileError is not null)
        {
            return await PersistValidationAsync(
                ReferenceDataImportResource.Locations,
                prepared.FileName,
                prepared.FileHash,
                mode,
                Array.Empty<ValidatedLocationImportRow>(),
                [parsed.FileError],
                0,
                actor,
                cancellationToken);
        }

        var errors = new List<ReferenceDataImportError>();
        var candidates = new List<LocationImportCandidate>();
        foreach (var record in parsed.Records)
        {
            if (!HasExpectedFieldCount(
                    record,
                    parsed.Format == CsvInputFormat.Canonical
                        ? ReferenceDataCsvSchemas.LocationHeaders.Count
                        : ReferenceDataCsvSchemas.LocationCompatibilityHeaders.Count,
                    errors))
            {
                continue;
            }

            var before = errors.Count;
            var canonical = parsed.Format == CsvInputFormat.Canonical;
            if (canonical)
            {
                ValidateSchemaVersion(record, errors);
            }

            var code = Field(record, canonical ? 1 : 2);
            var name = Field(record, canonical ? 2 : 0);
            var type = EmptyToNull(Field(record, canonical ? 3 : 1));
            var parentCode = canonical ? EmptyToNull(Field(record, 4)) : null;
            var isActiveText = canonical ? Field(record, 5) : "true";
            ValidateRequired(name, "name", 150, record.RowNumber, errors);
            ValidateOptional(type, "type", 80, record.RowNumber, errors);
            ValidateOptional(parentCode, "parentCode", 50, record.RowNumber, errors);
            var keyGenerated = string.IsNullOrWhiteSpace(code);
            if (keyGenerated && !string.IsNullOrWhiteSpace(name))
            {
                code = GenerateCode(name);
            }

            ValidateRequired(
                code,
                "code",
                50,
                record.RowNumber,
                errors,
                "Code is required and could not be generated from the name.");
            var isActive = ParseBoolean(
                isActiveText,
                "isActive",
                record.RowNumber,
                errors);
            if (!string.IsNullOrWhiteSpace(code) &&
                parentCode is not null &&
                NormalizeKey(code) == NormalizeKey(parentCode))
            {
                AddError(
                    errors,
                    record.RowNumber,
                    "parentCode",
                    "invalid_parent_location",
                    "A location cannot be its own parent.",
                    parentCode);
            }

            if (errors.Count != before || isActive is null)
            {
                continue;
            }

            candidates.Add(
                new LocationImportCandidate(
                    record.RowNumber,
                    keyGenerated,
                    code,
                    NormalizeKey(code),
                    name,
                    type,
                    parentCode,
                    parentCode is null ? null : NormalizeKey(parentCode),
                    isActive.Value));
        }

        MarkDuplicateKeys(
            candidates.Select(
                candidate => new ImportKey(
                    candidate.RowNumber,
                    candidate.Code,
                    candidate.CodeNormalized)),
            "code",
            "duplicate_location_code_in_file",
            "Location code appears more than once in the file.",
            errors);
        var usable = candidates
            .Where(candidate => !HasRowError(errors, candidate.RowNumber))
            .ToArray();
        var existing = await _repository.GetLocationsAsync(
            forUpdate: false,
            cancellationToken);
        var existingByCode = existing.ToDictionary(
            location => location.CodeNormalized,
            StringComparer.Ordinal);
        var preliminary = new List<ValidatedLocationImportRow>();
        foreach (var candidate in usable)
        {
            existingByCode.TryGetValue(candidate.CodeNormalized, out var matched);
            if (mode == ReferenceDataImportMode.CreateOnly && matched is not null)
            {
                AddError(
                    errors,
                    candidate.RowNumber,
                    "code",
                    "location_already_exists",
                    "A location with this code already exists.",
                    candidate.Code);
                continue;
            }

            preliminary.Add(
                new ValidatedLocationImportRow(
                    candidate.RowNumber,
                    matched is null
                        ? ReferenceDataImportAction.Create
                        : ReferenceDataImportAction.Update,
                    candidate.KeyGenerated,
                    matched?.Id,
                    matched?.Version,
                    candidate.Code,
                    candidate.CodeNormalized,
                    candidate.Name,
                    candidate.Type,
                    candidate.ParentCode,
                    candidate.ParentCodeNormalized,
                    candidate.IsActive));
        }

        ValidateLocationGraph(preliminary, existing, errors);
        var rows = preliminary
            .Where(row => !HasRowError(errors, row.RowNumber))
            .ToArray();
        return await PersistValidationAsync(
            ReferenceDataImportResource.Locations,
            prepared.FileName,
            prepared.FileHash,
            mode,
            rows,
            errors,
            parsed.Records.Count,
            actor,
            cancellationToken);
    }

    private static void MarkDuplicatePeople(
        IReadOnlyList<PersonImportCandidate> candidates,
        ICollection<ReferenceDataImportError> errors)
    {
        MarkDuplicateKeys(
            candidates.Select(
                candidate => new ImportKey(
                    candidate.RowNumber,
                    candidate.ExternalId,
                    candidate.ExternalIdNormalized)),
            "externalId",
            "duplicate_person_external_id_in_file",
            "Person external ID appears more than once in the file.",
            errors);

        foreach (var duplicate in candidates
                     .Where(candidate => candidate.EmailNormalized is not null)
                     .GroupBy(
                         candidate => candidate.EmailNormalized!,
                         StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            foreach (var candidate in duplicate)
            {
                AddError(
                    errors,
                    candidate.RowNumber,
                    "email",
                    "duplicate_person_email_in_file",
                    "Person email appears more than once in the file.",
                    candidate.Email);
            }
        }
    }

    private static void MarkDuplicateKeys(
        IEnumerable<ImportKey> keys,
        string column,
        string code,
        string message,
        ICollection<ReferenceDataImportError> errors)
    {
        foreach (var duplicate in keys
                     .GroupBy(key => key.Normalized, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            foreach (var key in duplicate)
            {
                AddError(
                    errors,
                    key.RowNumber,
                    column,
                    code,
                    message,
                    key.Value);
            }
        }
    }

    private static void ValidateLocationGraph(
        IReadOnlyList<ValidatedLocationImportRow> importedRows,
        IReadOnlyList<Location> existing,
        ICollection<ReferenceDataImportError> errors)
    {
        var existingById = existing.ToDictionary(location => location.Id);
        var importedByCode = importedRows.ToDictionary(
            row => row.CodeNormalized,
            StringComparer.Ordinal);
        var graph = existing.ToDictionary(
            location => location.CodeNormalized,
            location => new LocationGraphNode(
                location.CodeNormalized,
                location.ParentLocationId.HasValue &&
                existingById.TryGetValue(location.ParentLocationId.Value, out var parent)
                    ? parent.CodeNormalized
                    : null,
                location.IsActive),
            StringComparer.Ordinal);
        foreach (var row in importedRows)
        {
            graph[row.CodeNormalized] = new LocationGraphNode(
                row.CodeNormalized,
                row.ParentCodeNormalized,
                row.IsActive);
        }

        foreach (var row in importedRows)
        {
            if (row.ParentCodeNormalized is not null &&
                !graph.ContainsKey(row.ParentCodeNormalized))
            {
                AddError(
                    errors,
                    row.RowNumber,
                    "parentCode",
                    "parent_location_not_found",
                    "The parent location code does not exist in current data or this file.",
                    row.ParentCode);
            }
        }

        foreach (var node in graph.Values)
        {
            if (node.ParentCodeNormalized is null ||
                !graph.TryGetValue(node.ParentCodeNormalized, out var parent) ||
                !node.IsActive ||
                parent.IsActive)
            {
                continue;
            }

            if (importedByCode.TryGetValue(node.CodeNormalized, out var childRow))
            {
                AddError(
                    errors,
                    childRow.RowNumber,
                    "parentCode",
                    "inactive_parent_location",
                    "An active location must have an active parent.",
                    childRow.ParentCode);
            }

            if (importedByCode.TryGetValue(parent.CodeNormalized, out var parentRow))
            {
                AddError(
                    errors,
                    parentRow.RowNumber,
                    "isActive",
                    "location_has_active_children",
                    "Deactivate or move active child locations before deactivating this location.",
                    "false");
            }
        }

        foreach (var cycleCode in FindLocationCycleCodes(graph))
        {
            if (importedByCode.TryGetValue(cycleCode, out var row))
            {
                AddError(
                    errors,
                    row.RowNumber,
                    "parentCode",
                    "location_cycle",
                    "The selected parents create a location cycle.",
                    row.ParentCode);
            }
        }
    }

    private static HashSet<string> FindLocationCycleCodes(
        IReadOnlyDictionary<string, LocationGraphNode> graph)
    {
        var cycles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in graph.Keys)
        {
            var path = new List<string>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            var current = start;
            while (graph.TryGetValue(current, out var node) &&
                   node.ParentCodeNormalized is not null)
            {
                if (positions.TryGetValue(current, out var cycleStart))
                {
                    foreach (var code in path.Skip(cycleStart))
                    {
                        cycles.Add(code);
                    }

                    break;
                }

                positions[current] = path.Count;
                path.Add(current);
                current = node.ParentCodeNormalized;
            }
        }

        return cycles;
    }

    private static bool HasExpectedFieldCount(
        CsvSourceRecord record,
        int expected,
        ICollection<ReferenceDataImportError> errors)
    {
        if (record.Values.Count == expected)
        {
            return true;
        }

        AddError(
            errors,
            record.RowNumber,
            "row",
            "invalid_column_count",
            $"The row has {record.Values.Count} fields; {expected} are required.",
            null);
        return false;
    }

    private static void ValidateSchemaVersion(
        CsvSourceRecord record,
        ICollection<ReferenceDataImportError> errors)
    {
        var value = Field(record, 0);
        if (value != ReferenceDataCsvSchemas.SupportedSchemaVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture))
        {
            AddError(
                errors,
                record.RowNumber,
                "schemaVersion",
                "unsupported_schema_version",
                "Only reference-data CSV schema version 1 is supported.",
                value);
        }
    }

    private static PersonType? ParsePersonType(
        string value,
        int rowNumber,
        ICollection<ReferenceDataImportError> errors)
    {
        if (Enum.TryParse<PersonType>(value, ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        AddError(
            errors,
            rowNumber,
            "personType",
            "invalid_person_type",
            "Person type must be Staff, Pupil, Contractor, or Other.",
            value);
        return null;
    }

    private static bool? ParseBoolean(
        string value,
        string column,
        int rowNumber,
        ICollection<ReferenceDataImportError> errors)
    {
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        AddError(
            errors,
            rowNumber,
            column,
            "invalid_boolean",
            $"{column} must be true or false.",
            value);
        return null;
    }

    private static void ValidateRequired(
        string value,
        string column,
        int maximumLength,
        int rowNumber,
        ICollection<ReferenceDataImportError> errors,
        string? requiredMessage = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(
                errors,
                rowNumber,
                column,
                "required",
                requiredMessage ?? $"{column} is required.",
                value);
        }
        else
        {
            ValidateOptional(value, column, maximumLength, rowNumber, errors);
        }
    }

    private static void ValidateOptional(
        string? value,
        string column,
        int maximumLength,
        int rowNumber,
        ICollection<ReferenceDataImportError> errors)
    {
        if (value is not null && value.Length > maximumLength)
        {
            AddError(
                errors,
                rowNumber,
                column,
                "maximum_length",
                $"{column} must be {maximumLength} characters or fewer.",
                value);
        }
    }

    private static string Field(CsvSourceRecord record, int index) =>
        AssetCsvCodec.RemoveFormulaProtection(record.Values[index]).Trim();

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string NormalizeKey(string value) => value.Trim().ToUpperInvariant();

    private static string EmailLocalPart(string email)
    {
        var separator = email.IndexOf('@', StringComparison.Ordinal);
        return separator > 0 ? email[..separator].Trim() : string.Empty;
    }

    private static string GenerateCode(string name)
    {
        var builder = new System.Text.StringBuilder();
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

    private static void EnsureMode(ReferenceDataImportMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new RequestValidationException(
                "invalid_import_mode",
                "Import mode must be createOnly or upsertByKey.");
        }
    }

    private static bool HasRowError(
        IEnumerable<ReferenceDataImportError> errors,
        int rowNumber) =>
        errors.Any(error => error.RowNumber == rowNumber);

    private static void AddError(
        ICollection<ReferenceDataImportError> errors,
        int rowNumber,
        string column,
        string code,
        string message,
        string? value)
    {
        if (errors.Any(
                error =>
                    error.RowNumber == rowNumber &&
                    error.Column == column &&
                    error.Code == code))
        {
            return;
        }

        errors.Add(new ReferenceDataImportError(
            rowNumber,
            column,
            code,
            message,
            value));
    }

    private sealed record PersonImportCandidate(
        int RowNumber,
        bool KeyGenerated,
        string ExternalId,
        string ExternalIdNormalized,
        string DisplayName,
        PersonType PersonType,
        string? Email,
        string? EmailNormalized,
        bool IsActive);

    private sealed record DepartmentImportCandidate(
        int RowNumber,
        bool KeyGenerated,
        string Code,
        string CodeNormalized,
        string Name,
        bool IsActive);

    private sealed record LocationImportCandidate(
        int RowNumber,
        bool KeyGenerated,
        string Code,
        string CodeNormalized,
        string Name,
        string? Type,
        string? ParentCode,
        string? ParentCodeNormalized,
        bool IsActive);

    private sealed record ImportKey(int RowNumber, string Value, string Normalized);

    private sealed record LocationGraphNode(
        string CodeNormalized,
        string? ParentCodeNormalized,
        bool IsActive);
}
