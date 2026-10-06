using Inventory.Application.Abstractions;
using Inventory.Application.Assignments;
using Inventory.Application.Common;
using Inventory.Application.People;
using Inventory.Domain.Assignments;
using Inventory.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class EfCustodyRepository : ICustodyRepository
{
    private readonly InventoryDbContext _dbContext;

    public EfCustodyRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Person?> GetPersonAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.People.SingleOrDefaultAsync(person => person.Id == id, cancellationToken);

    public Task<PersonView?> GetPersonViewAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        _dbContext.People
            .AsNoTracking()
            .Where(person => person.Id == id)
            .Select(person => new PersonView(
                person.Id,
                person.ExternalId,
                person.DisplayName,
                person.Type,
                person.Email,
                person.IsActive,
                person.Version))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PageResult<PersonView>> SearchPeopleAsync(
        PersonSearch search,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.People.AsNoTracking();
        if (!search.IncludeInactive)
        {
            query = query.Where(person => person.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search.Query))
        {
            var term = search.Query;
            query = query.Where(person =>
                person.DisplayName.Contains(term) ||
                (person.ExternalId != null && person.ExternalId.Contains(term)) ||
                (person.Email != null && person.Email.Contains(term)));
        }

        if (search.Type.HasValue)
        {
            query = query.Where(person => person.Type == search.Type.Value);
        }

        query = search.Sort switch
        {
            "-displayName" => query.OrderByDescending(person => person.DisplayName),
            "externalId" => query
                .OrderBy(person => person.ExternalId)
                .ThenBy(person => person.DisplayName),
            "-externalId" => query
                .OrderByDescending(person => person.ExternalId)
                .ThenBy(person => person.DisplayName),
            "type" => query
                .OrderBy(person => person.Type)
                .ThenBy(person => person.DisplayName),
            "-type" => query
                .OrderByDescending(person => person.Type)
                .ThenBy(person => person.DisplayName),
            _ => query.OrderBy(person => person.DisplayName),
        };

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(person => new PersonView(
                person.Id,
                person.ExternalId,
                person.DisplayName,
                person.Type,
                person.Email,
                person.IsActive,
                person.Version))
            .ToArrayAsync(cancellationToken);
        return new PageResult<PersonView>(items, search.Page, search.PageSize, totalCount);
    }

    public Task<bool> PersonExternalIdExistsAsync(
        string normalizedExternalId,
        Guid? excludingPersonId,
        CancellationToken cancellationToken) =>
        _dbContext.People.AnyAsync(
            person =>
                person.ExternalIdNormalized == normalizedExternalId &&
                (!excludingPersonId.HasValue || person.Id != excludingPersonId.Value),
            cancellationToken);

    public Task<bool> PersonHasOpenAssignmentsAsync(
        Guid personId,
        CancellationToken cancellationToken) =>
        _dbContext.AssetAssignments.AnyAsync(
            assignment =>
                assignment.PersonId == personId &&
                assignment.ReturnedAtUtc == null,
            cancellationToken);

    public Task<bool> PersonHasBlockingLeaverCaseAsync(
        Guid personId,
        CancellationToken cancellationToken) =>
        _dbContext.LeaverCases.AnyAsync(
            leaverCase =>
                leaverCase.PersonId == personId &&
                (leaverCase.Status == LeaverCaseStatus.Open ||
                    leaverCase.Status == LeaverCaseStatus.Completed),
            cancellationToken);

    public Task<Department?> GetDepartmentAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        _dbContext.Departments.SingleOrDefaultAsync(
            department => department.Id == id,
            cancellationToken);

    public Task<DepartmentView?> GetDepartmentViewAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        _dbContext.Departments
            .AsNoTracking()
            .Where(department => department.Id == id)
            .Select(department => new DepartmentView(
                department.Id,
                department.Code,
                department.Name,
                department.IsActive,
                department.Version))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PageResult<DepartmentView>> SearchDepartmentsAsync(
        DepartmentSearch search,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Departments.AsNoTracking();
        if (!search.IncludeInactive)
        {
            query = query.Where(department => department.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search.Query))
        {
            var term = search.Query;
            query = query.Where(department =>
                department.Code.Contains(term) ||
                department.Name.Contains(term));
        }

        query = search.Sort switch
        {
            "-name" => query.OrderByDescending(department => department.Name),
            "code" => query.OrderBy(department => department.Code),
            "-code" => query.OrderByDescending(department => department.Code),
            _ => query.OrderBy(department => department.Name),
        };

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(department => new DepartmentView(
                department.Id,
                department.Code,
                department.Name,
                department.IsActive,
                department.Version))
            .ToArrayAsync(cancellationToken);
        return new PageResult<DepartmentView>(
            items,
            search.Page,
            search.PageSize,
            totalCount);
    }

    public Task<bool> DepartmentCodeExistsAsync(
        string normalizedCode,
        Guid? excludingDepartmentId,
        CancellationToken cancellationToken) =>
        _dbContext.Departments.AnyAsync(
            department =>
                department.CodeNormalized == normalizedCode &&
                (!excludingDepartmentId.HasValue ||
                 department.Id != excludingDepartmentId.Value),
            cancellationToken);

    public Task<bool> DepartmentHasOpenAssignmentsAsync(
        Guid departmentId,
        CancellationToken cancellationToken) =>
        _dbContext.AssetAssignments.AnyAsync(
            assignment =>
                assignment.DepartmentId == departmentId &&
                assignment.ReturnedAtUtc == null,
            cancellationToken);

    public Task<AssetAssignment?> GetAssignmentAsync(
        Guid assetId,
        Guid assignmentId,
        CancellationToken cancellationToken) =>
        _dbContext.AssetAssignments.SingleOrDefaultAsync(
            assignment =>
                assignment.AssetId == assetId &&
                assignment.Id == assignmentId,
            cancellationToken);

    public Task<AssetAssignment?> GetOpenAssignmentAsync(
        Guid assetId,
        CancellationToken cancellationToken) =>
        _dbContext.AssetAssignments.SingleOrDefaultAsync(
            assignment =>
                assignment.AssetId == assetId &&
                assignment.ReturnedAtUtc == null,
            cancellationToken);

    public Task<AssetAssignmentView?> GetAssignmentViewAsync(
        Guid assignmentId,
        CancellationToken cancellationToken) =>
        BuildAssignmentViewQuery(assignmentId: assignmentId)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PageResult<AssetAssignmentView>> GetAssetAssignmentsAsync(
        Guid assetId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = BuildAssignmentViewQuery(assetId: assetId, orderByNewest: true);
        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        return new PageResult<AssetAssignmentView>(items, page, pageSize, totalCount);
    }

    public async Task<PageResult<HolderAssetView>> GetHolderAssetsAsync(
        Guid? personId,
        Guid? departmentId,
        bool includeHistory,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query =
            from assignment in _dbContext.AssetAssignments.AsNoTracking()
            join asset in _dbContext.Assets.AsNoTracking()
                on assignment.AssetId equals asset.Id
            join assetLocation in _dbContext.Locations.AsNoTracking()
                on asset.LocationId equals assetLocation.Id into assetLocationJoin
            from assetLocation in assetLocationJoin.DefaultIfEmpty()
            join assignedLocation in _dbContext.Locations.AsNoTracking()
                on assignment.AssignedLocationId equals assignedLocation.Id
                into assignedLocationJoin
            from assignedLocation in assignedLocationJoin.DefaultIfEmpty()
            join returnLocation in _dbContext.Locations.AsNoTracking()
                on assignment.ReturnLocationId equals returnLocation.Id
                into returnLocationJoin
            from returnLocation in returnLocationJoin.DefaultIfEmpty()
            join person in _dbContext.People.AsNoTracking()
                on assignment.PersonId equals (Guid?)person.Id into personJoin
            from person in personJoin.DefaultIfEmpty()
            join department in _dbContext.Departments.AsNoTracking()
                on assignment.DepartmentId equals (Guid?)department.Id into departmentJoin
            from department in departmentJoin.DefaultIfEmpty()
            select new
            {
                Assignment = assignment,
                Asset = asset,
                AssetLocation = assetLocation,
                AssignedLocation = assignedLocation,
                ReturnLocation = returnLocation,
                Person = person,
                Department = department,
            };

        if (personId.HasValue)
        {
            query = query.Where(row => row.Assignment.PersonId == personId.Value);
        }

        if (departmentId.HasValue)
        {
            query = query.Where(row => row.Assignment.DepartmentId == departmentId.Value);
        }

        if (!includeHistory)
        {
            query = query.Where(row => row.Assignment.ReturnedAtUtc == null);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(row => row.Assignment.ReturnedAtUtc != null)
            .ThenByDescending(row => row.Assignment.AssignedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new HolderAssetView(
                row.Asset.Id,
                row.Asset.AssetTag,
                row.Asset.Status,
                row.Asset.Condition,
                row.Asset.LocationId,
                row.AssetLocation == null ? null : row.AssetLocation.Code,
                row.AssetLocation == null ? null : row.AssetLocation.Name,
                row.Asset.Version,
                new AssetAssignmentView(
                    row.Assignment.Id,
                    row.Assignment.AssetId,
                    row.Assignment.PersonId.HasValue
                        ? AssignmentTargetType.Person
                        : AssignmentTargetType.Department,
                    row.Assignment.PersonId,
                    row.Assignment.DepartmentId,
                    row.Assignment.ReturnedAtUtc == null &&
                    row.Assignment.PersonId.HasValue &&
                    row.Person != null
                        ? row.Person.DisplayName
                        : row.Assignment.ReturnedAtUtc == null &&
                          row.Department != null
                            ? row.Department.Name
                            : row.Assignment.TargetDisplayName,
                    row.Assignment.ReturnedAtUtc == null &&
                    row.Assignment.PersonId.HasValue &&
                    row.Person != null
                        ? row.Person.ExternalId
                        : row.Assignment.ReturnedAtUtc == null &&
                          row.Department != null
                            ? row.Department.Code
                            : row.Assignment.TargetReference,
                    row.Assignment.AssignedAtUtc,
                    row.Assignment.AssignedByUserId,
                    row.Assignment.AssignedByDisplayName,
                    row.Assignment.AssignedCondition,
                    row.Assignment.AssignedLocationId,
                    row.AssignedLocation == null ? null : row.AssignedLocation.Code,
                    row.AssignedLocation == null ? null : row.AssignedLocation.Name,
                    row.Assignment.AssignmentNotes,
                    row.Assignment.ReturnedAtUtc,
                    row.Assignment.ReturnedByUserId,
                    row.Assignment.ReturnedByDisplayName,
                    row.Assignment.ReturnCondition,
                    row.Assignment.ReturnLocationId,
                    row.ReturnLocation == null ? null : row.ReturnLocation.Code,
                    row.ReturnLocation == null ? null : row.ReturnLocation.Name,
                    row.Assignment.ReturnNotes,
                    row.Assignment.Version,
                    row.Assignment.ExpectedReturnOn,
                    false)))
            .ToArrayAsync(cancellationToken);
        return new PageResult<HolderAssetView>(items, page, pageSize, totalCount);
    }

    public async Task<PageResult<OverdueLoanView>> SearchOverdueLoansAsync(
        DateOnly schoolToday,
        Guid? personId,
        Guid? departmentId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query =
            from assignment in _dbContext.AssetAssignments.AsNoTracking()
            join asset in _dbContext.Assets.AsNoTracking()
                on assignment.AssetId equals asset.Id
            join person in _dbContext.People.AsNoTracking()
                on assignment.PersonId equals (Guid?)person.Id into personJoin
            from person in personJoin.DefaultIfEmpty()
            join department in _dbContext.Departments.AsNoTracking()
                on assignment.DepartmentId equals (Guid?)department.Id into departmentJoin
            from department in departmentJoin.DefaultIfEmpty()
            where assignment.ReturnedAtUtc == null &&
                  assignment.ExpectedReturnOn != null &&
                  assignment.ExpectedReturnOn < schoolToday
            select new
            {
                Assignment = assignment,
                Asset = asset,
                Person = person,
                Department = department,
            };

        if (personId.HasValue)
        {
            query = query.Where(row => row.Assignment.PersonId == personId.Value);
        }

        if (departmentId.HasValue)
        {
            query = query.Where(row => row.Assignment.DepartmentId == departmentId.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderBy(row => row.Assignment.ExpectedReturnOn)
            .ThenBy(row => row.Asset.AssetTag)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new OverdueLoanRow(
                row.Asset.Id,
                row.Asset.AssetTag,
                row.Asset.DeviceName,
                row.Assignment.Id,
                row.Assignment.PersonId.HasValue
                    ? AssignmentTargetType.Person
                    : AssignmentTargetType.Department,
                row.Assignment.PersonId ?? row.Assignment.DepartmentId!.Value,
                row.Assignment.PersonId.HasValue && row.Person != null
                    ? row.Person.DisplayName
                    : row.Assignment.DepartmentId.HasValue && row.Department != null
                        ? row.Department.Name
                        : row.Assignment.TargetDisplayName,
                row.Assignment.PersonId.HasValue && row.Person != null
                    ? row.Person.ExternalId
                    : row.Assignment.DepartmentId.HasValue && row.Department != null
                        ? row.Department.Code
                        : row.Assignment.TargetReference,
                row.Assignment.AssignedAtUtc,
                row.Assignment.ExpectedReturnOn!.Value,
                row.Assignment.Version,
                row.Asset.Version))
            .ToArrayAsync(cancellationToken);
        var items = rows
            .Select(row => new OverdueLoanView(
                row.AssetId,
                row.AssetTag,
                row.DeviceName,
                row.AssignmentId,
                row.TargetType,
                row.TargetId,
                row.TargetDisplayName,
                row.TargetReference,
                row.AssignedAtUtc,
                row.ExpectedReturnOn,
                schoolToday.DayNumber - row.ExpectedReturnOn.DayNumber,
                row.AssignmentVersion,
                row.AssetVersion))
            .ToArray();
        return new PageResult<OverdueLoanView>(items, page, pageSize, totalCount);
    }

    public void AddPerson(Person person) => _dbContext.People.Add(person);

    public void AddDepartment(Department department) =>
        _dbContext.Departments.Add(department);

    public void AddAssignment(AssetAssignment assignment) =>
        _dbContext.AssetAssignments.Add(assignment);

    public void AddAuditLog(Inventory.Domain.Auditing.AuditLog auditLog) =>
        _dbContext.AuditLogs.Add(auditLog);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesWithConflictMappingAsync(
                "data_conflict",
                "The requested change conflicts with existing data.",
                "The record was changed by another user.",
                cancellationToken);
        }
        catch (PersistenceConflictException exception)
            when (exception.GetBaseException().Message.Contains(
                "assignment_target_is_leaver",
                StringComparison.Ordinal))
        {
            throw new PersistenceConflictException(
                "assignment_target_is_leaver",
                "Assets cannot be assigned to a person with an open or completed leaver case.",
                exception);
        }
    }

    private IQueryable<AssetAssignmentView> BuildAssignmentViewQuery(
        Guid? assignmentId = null,
        Guid? assetId = null,
        bool orderByNewest = false)
    {
        IQueryable<AssetAssignment> assignments =
            _dbContext.AssetAssignments.AsNoTracking();
        if (assignmentId.HasValue)
        {
            assignments = assignments.Where(
                assignment => assignment.Id == assignmentId.Value);
        }

        if (assetId.HasValue)
        {
            assignments = assignments.Where(
                assignment => assignment.AssetId == assetId.Value);
        }

        if (orderByNewest)
        {
            assignments = assignments
                .OrderByDescending(assignment => assignment.AssignedAtUtc)
                .ThenByDescending(assignment => assignment.Id);
        }

        return
        from assignment in assignments
        join assignedLocation in _dbContext.Locations.AsNoTracking()
            on assignment.AssignedLocationId equals assignedLocation.Id
            into assignedLocationJoin
        from assignedLocation in assignedLocationJoin.DefaultIfEmpty()
        join returnLocation in _dbContext.Locations.AsNoTracking()
            on assignment.ReturnLocationId equals returnLocation.Id
            into returnLocationJoin
        from returnLocation in returnLocationJoin.DefaultIfEmpty()
        join person in _dbContext.People.AsNoTracking()
            on assignment.PersonId equals (Guid?)person.Id into personJoin
        from person in personJoin.DefaultIfEmpty()
        join department in _dbContext.Departments.AsNoTracking()
            on assignment.DepartmentId equals (Guid?)department.Id into departmentJoin
        from department in departmentJoin.DefaultIfEmpty()
        select new AssetAssignmentView(
            assignment.Id,
            assignment.AssetId,
            assignment.PersonId.HasValue
                ? AssignmentTargetType.Person
                : AssignmentTargetType.Department,
            assignment.PersonId,
            assignment.DepartmentId,
            assignment.ReturnedAtUtc == null &&
            assignment.PersonId.HasValue &&
            person != null
                ? person.DisplayName
                : assignment.ReturnedAtUtc == null &&
                  department != null
                    ? department.Name
                    : assignment.TargetDisplayName,
            assignment.ReturnedAtUtc == null &&
            assignment.PersonId.HasValue &&
            person != null
                ? person.ExternalId
                : assignment.ReturnedAtUtc == null &&
                  department != null
                    ? department.Code
                    : assignment.TargetReference,
            assignment.AssignedAtUtc,
            assignment.AssignedByUserId,
            assignment.AssignedByDisplayName,
            assignment.AssignedCondition,
            assignment.AssignedLocationId,
            assignedLocation == null ? null : assignedLocation.Code,
            assignedLocation == null ? null : assignedLocation.Name,
            assignment.AssignmentNotes,
            assignment.ReturnedAtUtc,
            assignment.ReturnedByUserId,
            assignment.ReturnedByDisplayName,
            assignment.ReturnCondition,
            assignment.ReturnLocationId,
            returnLocation == null ? null : returnLocation.Code,
            returnLocation == null ? null : returnLocation.Name,
            assignment.ReturnNotes,
            assignment.Version,
            assignment.ExpectedReturnOn,
            false);
    }

    private sealed record OverdueLoanRow(
        Guid AssetId,
        string AssetTag,
        string? DeviceName,
        Guid AssignmentId,
        AssignmentTargetType TargetType,
        Guid TargetId,
        string TargetDisplayName,
        string? TargetReference,
        DateTimeOffset AssignedAtUtc,
        DateOnly ExpectedReturnOn,
        Guid AssignmentVersion,
        Guid AssetVersion);
}
