using Inventory.Domain.Auditing;
using Inventory.Domain.Importing;
using Inventory.Domain.Locations;
using Inventory.Domain.People;

namespace Inventory.Application.Abstractions;

public interface IReferenceDataExchangeRepository
{
    Task<ReferenceDataImportJob?> GetImportJobAsync(
        Guid importJobId,
        bool forUpdate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Person>> GetPeopleAsync(
        IReadOnlyCollection<Guid> ids,
        IReadOnlyCollection<string> normalizedExternalIds,
        IReadOnlyCollection<string> normalizedEmails,
        bool forUpdate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetPeopleWithOpenAssignmentsAsync(
        IReadOnlyCollection<Guid> personIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Department>> GetDepartmentsAsync(
        IReadOnlyCollection<Guid> ids,
        IReadOnlyCollection<string> normalizedCodes,
        bool forUpdate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetDepartmentsWithOpenAssignmentsAsync(
        IReadOnlyCollection<Guid> departmentIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Location>> GetLocationsAsync(
        bool forUpdate,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Person>> ExportPeopleAsync(
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Department>> ExportDepartmentsAsync(
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Location>> ExportLocationsAsync(
        int take,
        CancellationToken cancellationToken);

    void AddImportJob(ReferenceDataImportJob job);

    void AddPerson(Person person);

    void AddDepartment(Department department);

    void AddLocation(Location location);

    void AddAuditLog(AuditLog auditLog);

    Task SaveRejectedCommitAuditAfterFailureAsync(
        AuditLog auditLog,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
