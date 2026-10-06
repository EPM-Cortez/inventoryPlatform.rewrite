using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Assignments;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Auditing;
using Inventory.Domain.People;

namespace Inventory.Application.People;

public sealed class DepartmentService
{
    private readonly ICustodyRepository _repository;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISchoolSettingsProvider _schoolSettingsProvider;

    public DepartmentService(
        ICustodyRepository repository,
        IClock clock,
        ICurrentUserContext currentUser,
        ISchoolSettingsProvider schoolSettingsProvider)
    {
        _repository = repository;
        _clock = clock;
        _currentUser = currentUser;
        _schoolSettingsProvider = schoolSettingsProvider;
    }

    public Task<PageResult<DepartmentView>> SearchAsync(
        DepartmentSearch search,
        CancellationToken cancellationToken)
    {
        var page = new PageRequest(search.Page, search.PageSize).Normalize();
        return _repository.SearchDepartmentsAsync(
            search with
            {
                Query = NormalizeOptional(search.Query),
                Page = page.Page,
                PageSize = page.PageSize,
                Sort = NormalizeSort(search.Sort),
            },
            cancellationToken);
    }

    public async Task<DepartmentView> GetAsync(
        Guid departmentId,
        CancellationToken cancellationToken) =>
        await _repository.GetDepartmentViewAsync(departmentId, cancellationToken)
        ?? throw new ResourceNotFoundException("Department", departmentId);

    public async Task<DepartmentView> CreateAsync(
        CreateDepartmentCommand command,
        CancellationToken cancellationToken)
    {
        await EnsureCodeAvailableAsync(command.Code, null, cancellationToken);

        Department department;
        try
        {
            department = Department.Create(command.Code, command.Name);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _repository.AddDepartment(department);
        var actor = _currentUser.RequireActor();
        _repository.AddAuditLog(AuditLog.Create(
            actor.UserId,
            actor.DisplayName,
            "department.created",
            "department",
            department.Id.ToString(),
            _clock.UtcNow,
            "succeeded",
            JsonSerializer.Serialize(new
            {
                department.Code,
                department.Name,
                department.IsActive,
            }),
            actor.IpAddress,
            actor.Client,
            actor.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        return await GetAsync(department.Id, cancellationToken);
    }

    public async Task<DepartmentView> UpdateAsync(
        Guid departmentId,
        UpdateDepartmentCommand command,
        CancellationToken cancellationToken)
    {
        var department = await _repository.GetDepartmentAsync(
                departmentId,
                cancellationToken)
            ?? throw new ResourceNotFoundException("Department", departmentId);

        await EnsureCodeAvailableAsync(command.Code, departmentId, cancellationToken);

        if (department.IsActive &&
            !command.IsActive &&
            await _repository.DepartmentHasOpenAssignmentsAsync(
                departmentId,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "holder_has_active_assignments",
                "A department with currently assigned assets cannot be made inactive.");
        }

        var before = new
        {
            department.Code,
            department.Name,
            department.IsActive,
        };
        try
        {
            department.Update(
                command.Code,
                command.Name,
                command.IsActive,
                command.Version);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        var actor = _currentUser.RequireActor();
        _repository.AddAuditLog(AuditLog.Create(
            actor.UserId,
            actor.DisplayName,
            "department.updated",
            "department",
            department.Id.ToString(),
            _clock.UtcNow,
            "succeeded",
            JsonSerializer.Serialize(new
            {
                Before = before,
                After = new
                {
                    department.Code,
                    department.Name,
                    department.IsActive,
                },
            }),
            actor.IpAddress,
            actor.Client,
            actor.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        return await GetAsync(department.Id, cancellationToken);
    }

    public async Task<PageResult<HolderAssetView>> GetAssetsAsync(
        Guid departmentId,
        bool includeHistory,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        _ = await _repository.GetDepartmentViewAsync(departmentId, cancellationToken)
            ?? throw new ResourceNotFoundException("Department", departmentId);
        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        var result = await _repository.GetHolderAssetsAsync(
            personId: null,
            departmentId,
            includeHistory,
            normalizedPage.Page,
            normalizedPage.PageSize,
            cancellationToken);
        return await WithOverdueStateAsync(result, cancellationToken);
    }

    private async Task EnsureCodeAvailableAsync(
        string? code,
        Guid? excludingDepartmentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new RequestValidationException("required", "Code is required.");
        }

        if (await _repository.DepartmentCodeExistsAsync(
                code.Trim().ToUpperInvariant(),
                excludingDepartmentId,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "duplicate_department_code",
                $"Department code '{code.Trim()}' is already in use.");
        }
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

    private static InventoryException ToApplicationException(DomainRuleException exception) =>
        exception.Code == "concurrency_conflict"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);

    private static string NormalizeSort(string? sort)
    {
        var normalized = string.IsNullOrWhiteSpace(sort) ? "name" : sort.Trim();
        return normalized switch
        {
            "name" or "-name" or "code" or "-code" => normalized,
            _ => throw new RequestValidationException(
                "invalid_sort",
                "Sort must be name, code, or a descending '-' form."),
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<PageResult<HolderAssetView>> WithOverdueStateAsync(
        PageResult<HolderAssetView> page,
        CancellationToken cancellationToken)
    {
        var schoolToday = await GetSchoolTodayAsync(cancellationToken);
        return page with
        {
            Items = page.Items
                .Select(
                    item => item with
                    {
                        Assignment = item.Assignment with
                        {
                            IsOverdue =
                                item.Assignment.ReturnedAtUtc is null &&
                                item.Assignment.ExpectedReturnOn.HasValue &&
                                item.Assignment.ExpectedReturnOn.Value < schoolToday,
                        },
                    })
                .ToArray(),
        };
    }

    private async Task<DateOnly> GetSchoolTodayAsync(
        CancellationToken cancellationToken)
    {
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        return SchoolBusinessDate.FromUtc(_clock.UtcNow, settings.TimeZone);
    }
}
