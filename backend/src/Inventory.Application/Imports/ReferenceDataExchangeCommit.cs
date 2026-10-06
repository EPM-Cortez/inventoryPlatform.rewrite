using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Importing;
using Inventory.Domain.Locations;
using Inventory.Domain.People;

namespace Inventory.Application.DataExchange;

public sealed partial class ReferenceDataExchangeService
{
    private async Task<CommitCounts> CommitPeopleAsync(
        ReferenceDataImportJob job,
        CancellationToken cancellationToken)
    {
        var rows = Deserialize<ValidatedPersonImportRow>(job.ValidatedRowsJson);
        EnsureSnapshotCounts(job, rows);
        EnsureUniqueKeys(
            rows.Select(row => (row.RowNumber, row.ExternalIdNormalized)),
            "The people import snapshot contains duplicate external IDs.");
        EnsureUniqueKeys(
            rows
                .Where(row => row.EmailNormalized is not null)
                .Select(row => (row.RowNumber, row.EmailNormalized!)),
            "The people import snapshot contains duplicate emails.");

        var people = await _repository.GetPeopleAsync(
            rows.Where(row => row.PersonId.HasValue)
                .Select(row => row.PersonId!.Value)
                .Distinct()
                .ToArray(),
            rows.Select(row => row.ExternalIdNormalized).Distinct().ToArray(),
            rows.Where(row => row.EmailNormalized is not null)
                .Select(row => row.EmailNormalized!)
                .Distinct()
                .ToArray(),
            forUpdate: true,
            cancellationToken);
        var byId = people.ToDictionary(person => person.Id);
        var byKey = people
            .Where(person => person.ExternalIdNormalized is not null)
            .ToDictionary(
                person => person.ExternalIdNormalized!,
                StringComparer.Ordinal);
        var byEmail = people
            .Where(person => person.Email is not null)
            .GroupBy(person => NormalizeKey(person.Email!), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Person>)group.ToArray(),
                StringComparer.Ordinal);

        var targets = new Dictionary<int, Person>();
        foreach (var row in rows)
        {
            ValidatePersonSnapshotRow(row);
            if (row.Action == ReferenceDataImportAction.Create)
            {
                if (row.PersonId.HasValue ||
                    row.ExpectedVersion.HasValue ||
                    byKey.ContainsKey(row.ExternalIdNormalized))
                {
                    throw SnapshotConflict(
                        "A person targeted for creation now exists or has invalid snapshot state.");
                }
            }
            else
            {
                if (!row.PersonId.HasValue ||
                    !row.ExpectedVersion.HasValue ||
                    !byId.TryGetValue(row.PersonId.Value, out var target) ||
                    target.Version != row.ExpectedVersion.Value ||
                    target.ExternalIdNormalized != row.ExternalIdNormalized)
                {
                    throw SnapshotConflict(
                        "A person targeted for update changed after validation.");
                }

                targets[row.RowNumber] = target;
            }

            if (row.EmailNormalized is not null &&
                byEmail.TryGetValue(row.EmailNormalized, out var emailMatches))
            {
                var targetId = row.PersonId;
                if (emailMatches.Any(person => person.Id != targetId))
                {
                    throw SnapshotConflict(
                        "An imported email now belongs to a different person.");
                }
            }
        }

        var deactivatedIds = rows
            .Where(row => !row.IsActive && row.PersonId.HasValue)
            .Select(row => row.PersonId!.Value)
            .ToArray();
        if ((await _repository.GetPeopleWithOpenAssignmentsAsync(
                deactivatedIds,
                cancellationToken)).Count > 0)
        {
            throw SnapshotConflict(
                "A person targeted for deactivation now has assigned assets.");
        }

        var created = 0;
        var updated = 0;
        var unchanged = 0;
        foreach (var row in rows)
        {
            try
            {
                if (row.Action == ReferenceDataImportAction.Create)
                {
                    var person = Person.Create(
                        row.ExternalId,
                        row.DisplayName,
                        row.PersonType,
                        row.Email);
                    if (!row.IsActive)
                    {
                        person.Update(
                            row.ExternalId,
                            row.DisplayName,
                            row.PersonType,
                            row.Email,
                            isActive: false,
                            person.Version);
                    }

                    _repository.AddPerson(person);
                    created++;
                    continue;
                }

                var target = targets[row.RowNumber];
                if (PersonMatches(target, row))
                {
                    unchanged++;
                    continue;
                }

                target.Update(
                    row.ExternalId,
                    row.DisplayName,
                    row.PersonType,
                    row.Email,
                    row.IsActive,
                    row.ExpectedVersion!.Value);
                updated++;
            }
            catch (DomainRuleException exception)
            {
                throw SnapshotConflict(exception.Message);
            }
        }

        return new CommitCounts(created, updated, unchanged);
    }

    private async Task<CommitCounts> CommitDepartmentsAsync(
        ReferenceDataImportJob job,
        CancellationToken cancellationToken)
    {
        var rows = Deserialize<ValidatedDepartmentImportRow>(job.ValidatedRowsJson);
        EnsureSnapshotCounts(job, rows);
        EnsureUniqueKeys(
            rows.Select(row => (row.RowNumber, row.CodeNormalized)),
            "The department import snapshot contains duplicate codes.");

        var departments = await _repository.GetDepartmentsAsync(
            rows.Where(row => row.DepartmentId.HasValue)
                .Select(row => row.DepartmentId!.Value)
                .Distinct()
                .ToArray(),
            rows.Select(row => row.CodeNormalized).Distinct().ToArray(),
            forUpdate: true,
            cancellationToken);
        var byId = departments.ToDictionary(department => department.Id);
        var byCode = departments.ToDictionary(
            department => department.CodeNormalized,
            StringComparer.Ordinal);
        var targets = new Dictionary<int, Department>();
        foreach (var row in rows)
        {
            ValidateDepartmentSnapshotRow(row);
            if (row.Action == ReferenceDataImportAction.Create)
            {
                if (row.DepartmentId.HasValue ||
                    row.ExpectedVersion.HasValue ||
                    byCode.ContainsKey(row.CodeNormalized))
                {
                    throw SnapshotConflict(
                        "A department targeted for creation now exists or has invalid snapshot state.");
                }
            }
            else
            {
                if (!row.DepartmentId.HasValue ||
                    !row.ExpectedVersion.HasValue ||
                    !byId.TryGetValue(row.DepartmentId.Value, out var target) ||
                    target.Version != row.ExpectedVersion.Value ||
                    target.CodeNormalized != row.CodeNormalized)
                {
                    throw SnapshotConflict(
                        "A department targeted for update changed after validation.");
                }

                targets[row.RowNumber] = target;
            }
        }

        var deactivatedIds = rows
            .Where(row => !row.IsActive && row.DepartmentId.HasValue)
            .Select(row => row.DepartmentId!.Value)
            .ToArray();
        if ((await _repository.GetDepartmentsWithOpenAssignmentsAsync(
                deactivatedIds,
                cancellationToken)).Count > 0)
        {
            throw SnapshotConflict(
                "A department targeted for deactivation now has assigned assets.");
        }

        var created = 0;
        var updated = 0;
        var unchanged = 0;
        foreach (var row in rows)
        {
            try
            {
                if (row.Action == ReferenceDataImportAction.Create)
                {
                    var department = Department.Create(row.Code, row.DepartmentName);
                    if (!row.IsActive)
                    {
                        department.Update(
                            row.Code,
                            row.DepartmentName,
                            isActive: false,
                            department.Version);
                    }

                    _repository.AddDepartment(department);
                    created++;
                    continue;
                }

                var target = targets[row.RowNumber];
                if (DepartmentMatches(target, row))
                {
                    unchanged++;
                    continue;
                }

                target.Update(
                    row.Code,
                    row.DepartmentName,
                    row.IsActive,
                    row.ExpectedVersion!.Value);
                updated++;
            }
            catch (DomainRuleException exception)
            {
                throw SnapshotConflict(exception.Message);
            }
        }

        return new CommitCounts(created, updated, unchanged);
    }

    private async Task<CommitCounts> CommitLocationsAsync(
        ReferenceDataImportJob job,
        CancellationToken cancellationToken)
    {
        var rows = Deserialize<ValidatedLocationImportRow>(job.ValidatedRowsJson);
        EnsureSnapshotCounts(job, rows);
        EnsureUniqueKeys(
            rows.Select(row => (row.RowNumber, row.CodeNormalized)),
            "The location import snapshot contains duplicate codes.");

        var locations = await _repository.GetLocationsAsync(
            forUpdate: true,
            cancellationToken);
        var byId = locations.ToDictionary(location => location.Id);
        var byCode = locations.ToDictionary(
            location => location.CodeNormalized,
            StringComparer.Ordinal);
        var targets = new Dictionary<int, Location>();
        foreach (var row in rows)
        {
            ValidateLocationSnapshotRow(row);
            if (row.Action == ReferenceDataImportAction.Create)
            {
                if (row.LocationId.HasValue ||
                    row.ExpectedVersion.HasValue ||
                    byCode.ContainsKey(row.CodeNormalized))
                {
                    throw SnapshotConflict(
                        "A location targeted for creation now exists or has invalid snapshot state.");
                }
            }
            else
            {
                if (!row.LocationId.HasValue ||
                    !row.ExpectedVersion.HasValue ||
                    !byId.TryGetValue(row.LocationId.Value, out var target) ||
                    target.Version != row.ExpectedVersion.Value ||
                    target.CodeNormalized != row.CodeNormalized)
                {
                    throw SnapshotConflict(
                        "A location targeted for update changed after validation.");
                }

                targets[row.RowNumber] = target;
            }
        }

        var graphErrors = new List<ReferenceDataImportError>();
        ValidateLocationGraph(rows, locations, graphErrors);
        if (graphErrors.Count > 0)
        {
            throw SnapshotConflict(graphErrors[0].Message);
        }

        var createdRows = new HashSet<int>();
        foreach (var row in rows.Where(
                     row => row.Action == ReferenceDataImportAction.Create))
        {
            try
            {
                var location = Location.Create(
                    row.Code,
                    row.LocationName,
                    row.LocationType);
                _repository.AddLocation(location);
                byCode.Add(row.CodeNormalized, location);
                targets[row.RowNumber] = location;
                createdRows.Add(row.RowNumber);
            }
            catch (DomainRuleException exception)
            {
                throw SnapshotConflict(exception.Message);
            }
        }

        var created = 0;
        var updated = 0;
        var unchanged = 0;
        foreach (var row in rows)
        {
            var target = targets[row.RowNumber];
            Guid? parentId = row.ParentCodeNormalized is null
                ? null
                : byCode[row.ParentCodeNormalized].Id;
            if (createdRows.Contains(row.RowNumber))
            {
                try
                {
                    if (parentId.HasValue || !row.IsActive)
                    {
                        target.Update(
                            row.Code,
                            row.LocationName,
                            row.LocationType,
                            parentId,
                            row.IsActive,
                            target.Version);
                    }
                }
                catch (DomainRuleException exception)
                {
                    throw SnapshotConflict(exception.Message);
                }

                created++;
                continue;
            }

            if (LocationMatches(target, row, parentId))
            {
                unchanged++;
                continue;
            }

            try
            {
                target.Update(
                    row.Code,
                    row.LocationName,
                    row.LocationType,
                    parentId,
                    row.IsActive,
                    row.ExpectedVersion!.Value);
                updated++;
            }
            catch (DomainRuleException exception)
            {
                throw SnapshotConflict(exception.Message);
            }
        }

        return new CommitCounts(created, updated, unchanged);
    }

    private static void EnsureSnapshotCounts<T>(
        ReferenceDataImportJob job,
        IReadOnlyList<T> rows)
        where T : IValidatedReferenceDataImportRow
    {
        if (rows.Count != job.ValidRows ||
            rows.Count(row => row.Action == ReferenceDataImportAction.Create) !=
            job.CreateRows ||
            rows.Count(row => row.Action == ReferenceDataImportAction.Update) !=
            job.UpdateRows)
        {
            throw SnapshotConflict(
                "The reference-data import snapshot counts are inconsistent.");
        }
    }

    private static void EnsureUniqueKeys(
        IEnumerable<(int RowNumber, string Key)> keys,
        string message)
    {
        if (keys.GroupBy(item => item.Key, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw SnapshotConflict(message);
        }
    }

    private static void ValidatePersonSnapshotRow(ValidatedPersonImportRow row)
    {
        if (string.IsNullOrWhiteSpace(row.ExternalId) ||
            row.ExternalId.Length > 100 ||
            NormalizeKey(row.ExternalId) != row.ExternalIdNormalized ||
            string.IsNullOrWhiteSpace(row.DisplayName) ||
            row.DisplayName.Length > 200 ||
            !Enum.IsDefined(row.PersonType) ||
            (row.Email is null) != (row.EmailNormalized is null) ||
            (row.Email is not null &&
             NormalizeKey(row.Email) != row.EmailNormalized))
        {
            throw SnapshotConflict("The people import snapshot contains invalid row data.");
        }
    }

    private static void ValidateDepartmentSnapshotRow(
        ValidatedDepartmentImportRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Code) ||
            row.Code.Length > 50 ||
            NormalizeKey(row.Code) != row.CodeNormalized ||
            string.IsNullOrWhiteSpace(row.DepartmentName) ||
            row.DepartmentName.Length > 150)
        {
            throw SnapshotConflict(
                "The department import snapshot contains invalid row data.");
        }
    }

    private static void ValidateLocationSnapshotRow(ValidatedLocationImportRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Code) ||
            row.Code.Length > 50 ||
            NormalizeKey(row.Code) != row.CodeNormalized ||
            string.IsNullOrWhiteSpace(row.LocationName) ||
            row.LocationName.Length > 150 ||
            (row.LocationType?.Length ?? 0) > 80 ||
            (row.ParentCode is null) != (row.ParentCodeNormalized is null) ||
            (row.ParentCode is not null &&
             NormalizeKey(row.ParentCode) != row.ParentCodeNormalized))
        {
            throw SnapshotConflict(
                "The location import snapshot contains invalid row data.");
        }
    }

    private static bool PersonMatches(Person person, ValidatedPersonImportRow row) =>
        person.ExternalId == row.ExternalId &&
        person.DisplayName == row.DisplayName &&
        person.Type == row.PersonType &&
        person.Email == row.Email &&
        person.IsActive == row.IsActive;

    private static bool DepartmentMatches(
        Department department,
        ValidatedDepartmentImportRow row) =>
        department.Code == row.Code &&
        department.Name == row.DepartmentName &&
        department.IsActive == row.IsActive;

    private static bool LocationMatches(
        Location location,
        ValidatedLocationImportRow row,
        Guid? parentId) =>
        location.Code == row.Code &&
        location.Name == row.LocationName &&
        location.Type == row.LocationType &&
        location.ParentLocationId == parentId &&
        location.IsActive == row.IsActive;

    private static ResourceConflictException SnapshotConflict(string message) =>
        new("import_snapshot_stale", message);
}
