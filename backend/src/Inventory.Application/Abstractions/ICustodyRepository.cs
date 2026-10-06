using Inventory.Application.Assignments;
using Inventory.Application.Common;
using Inventory.Application.People;
using Inventory.Domain.Assignments;
using Inventory.Domain.Auditing;
using Inventory.Domain.People;

namespace Inventory.Application.Abstractions;

public interface ICustodyRepository
{
    Task<Person?> GetPersonAsync(Guid id, CancellationToken cancellationToken);

    Task<PersonView?> GetPersonViewAsync(Guid id, CancellationToken cancellationToken);

    Task<PageResult<PersonView>> SearchPeopleAsync(
        PersonSearch search,
        CancellationToken cancellationToken);

    Task<bool> PersonExternalIdExistsAsync(
        string normalizedExternalId,
        Guid? excludingPersonId,
        CancellationToken cancellationToken);

    Task<bool> PersonHasOpenAssignmentsAsync(
        Guid personId,
        CancellationToken cancellationToken);

    Task<bool> PersonHasBlockingLeaverCaseAsync(
        Guid personId,
        CancellationToken cancellationToken);

    Task<Department?> GetDepartmentAsync(Guid id, CancellationToken cancellationToken);

    Task<DepartmentView?> GetDepartmentViewAsync(Guid id, CancellationToken cancellationToken);

    Task<PageResult<DepartmentView>> SearchDepartmentsAsync(
        DepartmentSearch search,
        CancellationToken cancellationToken);

    Task<bool> DepartmentCodeExistsAsync(
        string normalizedCode,
        Guid? excludingDepartmentId,
        CancellationToken cancellationToken);

    Task<bool> DepartmentHasOpenAssignmentsAsync(
        Guid departmentId,
        CancellationToken cancellationToken);

    Task<AssetAssignment?> GetAssignmentAsync(
        Guid assetId,
        Guid assignmentId,
        CancellationToken cancellationToken);

    Task<AssetAssignment?> GetOpenAssignmentAsync(
        Guid assetId,
        CancellationToken cancellationToken);

    Task<AssetAssignmentView?> GetAssignmentViewAsync(
        Guid assignmentId,
        CancellationToken cancellationToken);

    Task<PageResult<AssetAssignmentView>> GetAssetAssignmentsAsync(
        Guid assetId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<PageResult<HolderAssetView>> GetHolderAssetsAsync(
        Guid? personId,
        Guid? departmentId,
        bool includeHistory,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<PageResult<OverdueLoanView>> SearchOverdueLoansAsync(
        DateOnly schoolToday,
        Guid? personId,
        Guid? departmentId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    void AddPerson(Person person);

    void AddDepartment(Department department);

    void AddAssignment(AssetAssignment assignment);

    void AddAuditLog(AuditLog auditLog);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
