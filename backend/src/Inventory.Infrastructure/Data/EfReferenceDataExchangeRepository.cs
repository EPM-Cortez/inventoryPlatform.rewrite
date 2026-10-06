using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Auditing;
using Inventory.Domain.Importing;
using Inventory.Domain.Locations;
using Inventory.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class EfReferenceDataExchangeRepository : IReferenceDataExchangeRepository
{
    private readonly InventoryDbContext _dbContext;

    public EfReferenceDataExchangeRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ReferenceDataImportJob?> GetImportJobAsync(
        Guid importJobId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        IQueryable<ReferenceDataImportJob> query = _dbContext.ReferenceDataImportJobs;
        if (!forUpdate)
        {
            query = query.AsNoTracking();
        }

        return query.SingleOrDefaultAsync(
            job => job.Id == importJobId,
            cancellationToken);
    }

    // StringComparison overloads are not translated by both supported EF providers.
#pragma warning disable CA1304, CA1311, CA1862
    public async Task<IReadOnlyList<Person>> GetPeopleAsync(
        IReadOnlyCollection<Guid> ids,
        IReadOnlyCollection<string> normalizedExternalIds,
        IReadOnlyCollection<string> normalizedEmails,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0 &&
            normalizedExternalIds.Count == 0 &&
            normalizedEmails.Count == 0)
        {
            return [];
        }

        IQueryable<Person> query = _dbContext.People;
        if (!forUpdate)
        {
            query = query.AsNoTracking();
        }

        return await query
            .Where(
                person =>
                    ids.Contains(person.Id) ||
                    (person.ExternalIdNormalized != null &&
                     normalizedExternalIds.Contains(person.ExternalIdNormalized)) ||
                    (person.Email != null &&
                     normalizedEmails.Contains(person.Email.ToUpper())))
            .ToArrayAsync(cancellationToken);
    }
#pragma warning restore CA1304, CA1311, CA1862

    public async Task<IReadOnlyList<Guid>> GetPeopleWithOpenAssignmentsAsync(
        IReadOnlyCollection<Guid> personIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.AssetAssignments
            .AsNoTracking()
            .Where(
                assignment =>
                    assignment.ReturnedAtUtc == null &&
                    assignment.PersonId.HasValue &&
                    personIds.Contains(assignment.PersonId.Value))
            .Select(assignment => assignment.PersonId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Department>> GetDepartmentsAsync(
        IReadOnlyCollection<Guid> ids,
        IReadOnlyCollection<string> normalizedCodes,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0 && normalizedCodes.Count == 0)
        {
            return [];
        }

        IQueryable<Department> query = _dbContext.Departments;
        if (!forUpdate)
        {
            query = query.AsNoTracking();
        }

        return await query
            .Where(
                department =>
                    ids.Contains(department.Id) ||
                    normalizedCodes.Contains(department.CodeNormalized))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetDepartmentsWithOpenAssignmentsAsync(
        IReadOnlyCollection<Guid> departmentIds,
        CancellationToken cancellationToken)
    {
        if (departmentIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.AssetAssignments
            .AsNoTracking()
            .Where(
                assignment =>
                    assignment.ReturnedAtUtc == null &&
                    assignment.DepartmentId.HasValue &&
                    departmentIds.Contains(assignment.DepartmentId.Value))
            .Select(assignment => assignment.DepartmentId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Location>> GetLocationsAsync(
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        IQueryable<Location> query = _dbContext.Locations;
        if (!forUpdate)
        {
            query = query.AsNoTracking();
        }

        return await query
            .OrderBy(location => location.Code)
            .ThenBy(location => location.Id)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Person>> ExportPeopleAsync(
        int take,
        CancellationToken cancellationToken) =>
        await _dbContext.People
            .AsNoTracking()
            .OrderBy(person => person.ExternalIdNormalized)
            .ThenBy(person => person.DisplayName)
            .ThenBy(person => person.Id)
            .Take(take)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Department>> ExportDepartmentsAsync(
        int take,
        CancellationToken cancellationToken) =>
        await _dbContext.Departments
            .AsNoTracking()
            .OrderBy(department => department.CodeNormalized)
            .ThenBy(department => department.Id)
            .Take(take)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Location>> ExportLocationsAsync(
        int take,
        CancellationToken cancellationToken) =>
        await _dbContext.Locations
            .AsNoTracking()
            .OrderBy(location => location.CodeNormalized)
            .ThenBy(location => location.Id)
            .Take(take)
            .ToArrayAsync(cancellationToken);

    public void AddImportJob(ReferenceDataImportJob job) =>
        _dbContext.ReferenceDataImportJobs.Add(job);

    public void AddPerson(Person person) => _dbContext.People.Add(person);

    public void AddDepartment(Department department) =>
        _dbContext.Departments.Add(department);

    public void AddLocation(Location location) => _dbContext.Locations.Add(location);

    public void AddAuditLog(AuditLog auditLog) => _dbContext.AuditLogs.Add(auditLog);

    public async Task SaveRejectedCommitAuditAfterFailureAsync(
        AuditLog auditLog,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        _dbContext.AuditLogs.Add(auditLog);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (PersistenceExceptionMapping.IsConstraintViolation(exception))
        {
            throw new PersistenceConflictException(
                "import_rejection_audit_failed",
                "The rejected reference-data import could not be audited.",
                exception);
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesWithConflictMappingAsync(
            "import_data_conflict",
            "The reference-data import conflicts with current data.",
            "The reference-data import or one of its records changed.",
            cancellationToken);
}
