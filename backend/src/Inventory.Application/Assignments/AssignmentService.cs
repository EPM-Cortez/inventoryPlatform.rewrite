using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Auditing;
using Inventory.Domain.Common;

namespace Inventory.Application.Assignments;

public sealed class AssignmentService
{
    private static readonly JsonSerializerOptions EventJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IInventoryRepository _inventoryRepository;
    private readonly ICustodyRepository _custodyRepository;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISchoolSettingsProvider _schoolSettingsProvider;

    public AssignmentService(
        IInventoryRepository inventoryRepository,
        ICustodyRepository custodyRepository,
        IClock clock,
        ICurrentUserContext currentUser,
        ISchoolSettingsProvider schoolSettingsProvider)
    {
        _inventoryRepository = inventoryRepository;
        _custodyRepository = custodyRepository;
        _clock = clock;
        _currentUser = currentUser;
        _schoolSettingsProvider = schoolSettingsProvider;
    }

    public async Task<PageResult<AssetAssignmentView>> GetForAssetAsync(
        Guid assetId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        _ = await _inventoryRepository.GetAssetViewAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Asset", assetId);
        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        var result = await _custodyRepository.GetAssetAssignmentsAsync(
            assetId,
            normalizedPage.Page,
            normalizedPage.PageSize,
            cancellationToken);
        return await WithOverdueStateAsync(result, cancellationToken);
    }

    public async Task<AssignmentWorkflowResult> AssignOrTransferAsync(
        Guid assetId,
        AssignAssetCommand command,
        CancellationToken cancellationToken)
    {
        var asset = await _inventoryRepository.GetAssetAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Asset", assetId);
        var target = await ResolveTargetAsync(
            command.PersonId,
            command.DepartmentId,
            cancellationToken);
        var currentAssignment = await _custodyRepository.GetOpenAssignmentAsync(
            assetId,
            cancellationToken);

        if (currentAssignment is not null &&
            currentAssignment.PersonId == target.PersonId &&
            currentAssignment.DepartmentId == target.DepartmentId)
        {
            throw new ResourceConflictException(
                "already_assigned_to_target",
                $"Asset '{asset.AssetTag}' is already assigned to {target.DisplayName}.");
        }

        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        AssetEventType eventType;

        try
        {
            if (currentAssignment is null)
            {
                asset.Assign(command.AssetVersion, now);
                eventType = AssetEventType.Assigned;
            }
            else
            {
                asset.Transfer(command.AssetVersion, now);
                currentAssignment.Close(
                    currentAssignment.Version,
                    now,
                    actor.UserId,
                    actor.DisplayName,
                    asset.Condition,
                    asset.LocationId,
                    command.Notes);
                eventType = AssetEventType.Transferred;
            }
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        AssetAssignment newAssignment;
        try
        {
            newAssignment = AssetAssignment.Create(
                asset.Id,
                target.PersonId,
                target.DepartmentId,
                target.DisplayName,
                target.Reference,
                now,
                actor.UserId,
                actor.DisplayName,
                asset.Condition,
                asset.LocationId,
                command.Notes,
                command.ExpectedReturnOn);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _custodyRepository.AddAssignment(newAssignment);
        _inventoryRepository.AddAssetEvent(
            AssetEvent.Create(
                asset.Id,
                eventType,
                now,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                eventType == AssetEventType.Assigned
                    ? $"Asset {asset.AssetTag} was assigned to {target.DisplayName}."
                    : $"Asset {asset.AssetTag} was transferred to {target.DisplayName}.",
                JsonSerializer.Serialize(
                    new
                    {
                        assignmentId = newAssignment.Id,
                        previousAssignmentId = currentAssignment?.Id,
                        fromTarget = currentAssignment is null
                            ? null
                            : new
                            {
                                type = currentAssignment.TargetType,
                                id = currentAssignment.PersonId ??
                                     currentAssignment.DepartmentId,
                                displayName = currentAssignment.TargetDisplayName,
                                reference = currentAssignment.TargetReference,
                            },
                        toTarget = new
                        {
                            type = newAssignment.TargetType,
                            id = newAssignment.PersonId ?? newAssignment.DepartmentId,
                            displayName = newAssignment.TargetDisplayName,
                            reference = newAssignment.TargetReference,
                        },
                        targetType = newAssignment.TargetType,
                        targetId = newAssignment.PersonId ?? newAssignment.DepartmentId,
                        targetDisplayName = newAssignment.TargetDisplayName,
                        targetReference = newAssignment.TargetReference,
                        condition = newAssignment.AssignedCondition,
                        locationId = newAssignment.AssignedLocationId,
                        notes = newAssignment.AssignmentNotes,
                        expectedReturnOn = newAssignment.ExpectedReturnOn,
                    },
                    EventJsonOptions),
                actor.CorrelationId));

        await SaveChangesAsync(cancellationToken);
        return await BuildWorkflowResultAsync(newAssignment.Id, asset, cancellationToken);
    }

    public async Task<AssignmentWorkflowResult> ReturnAsync(
        Guid assetId,
        Guid assignmentId,
        ReturnAssetCommand command,
        CancellationToken cancellationToken)
    {
        var asset = await _inventoryRepository.GetAssetAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Asset", assetId);
        var assignment = await _custodyRepository.GetAssignmentAsync(
                assetId,
                assignmentId,
                cancellationToken)
            ?? throw new ResourceNotFoundException("Asset assignment", assignmentId);

        if (!assignment.IsOpen)
        {
            throw new ResourceConflictException(
                "assignment_already_closed",
                "The assignment has already been returned.");
        }

        if (command.LocationId.HasValue)
        {
            var location = await _inventoryRepository.GetLocationAsync(
                command.LocationId.Value,
                cancellationToken);
            if (location is null || !location.IsActive)
            {
                throw new RequestValidationException(
                    "invalid_location",
                    "The return location does not exist or is inactive.",
                    new Dictionary<string, string[]>
                    {
                        ["locationId"] = ["The return location does not exist or is inactive."],
                    });
            }
        }

        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;

        try
        {
            asset.Return(
                command.AssetVersion,
                now,
                command.Condition,
                command.LocationId);
            assignment.Close(
                command.AssignmentVersion,
                now,
                actor.UserId,
                actor.DisplayName,
                asset.Condition,
                asset.LocationId,
                command.Notes);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _inventoryRepository.AddAssetEvent(
            AssetEvent.Create(
                asset.Id,
                AssetEventType.Returned,
                now,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                $"Asset {asset.AssetTag} was returned from {assignment.TargetDisplayName}.",
                JsonSerializer.Serialize(
                    new
                    {
                        assignmentId = assignment.Id,
                        targetType = assignment.TargetType,
                        targetId = assignment.PersonId ?? assignment.DepartmentId,
                        targetDisplayName = assignment.TargetDisplayName,
                        targetReference = assignment.TargetReference,
                        condition = assignment.ReturnCondition,
                        locationId = assignment.ReturnLocationId,
                        notes = assignment.ReturnNotes,
                    },
                    EventJsonOptions),
                actor.CorrelationId));

        await SaveChangesAsync(cancellationToken);
        return await BuildWorkflowResultAsync(assignment.Id, asset, cancellationToken);
    }

    public async Task<AssetAssignmentView> UpdateExpectedReturnAsync(
        Guid assetId,
        Guid assignmentId,
        UpdateExpectedReturnCommand command,
        CancellationToken cancellationToken)
    {
        var asset = await _inventoryRepository.GetAssetAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Asset", assetId);
        var assignment = await _custodyRepository.GetAssignmentAsync(
                assetId,
                assignmentId,
                cancellationToken)
            ?? throw new ResourceNotFoundException("Asset assignment", assignmentId);

        var previous = assignment.ExpectedReturnOn;
        try
        {
            assignment.UpdateExpectedReturnOn(
                command.AssignmentVersion,
                command.ExpectedReturnOn);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        var details = JsonSerializer.Serialize(
            new
            {
                assignmentId,
                previousExpectedReturnOn = previous,
                expectedReturnOn = command.ExpectedReturnOn,
            },
            EventJsonOptions);
        _inventoryRepository.AddAssetEvent(
            AssetEvent.Create(
                asset.Id,
                AssetEventType.ExpectedReturnChanged,
                now,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                command.ExpectedReturnOn.HasValue
                    ? $"Expected return date for asset {asset.AssetTag} was set to " +
                      $"{command.ExpectedReturnOn:yyyy-MM-dd}."
                    : $"Expected return date for asset {asset.AssetTag} was cleared.",
                details,
                actor.CorrelationId));
        _inventoryRepository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "assignment.expected_return.updated",
                "asset_assignment",
                assignment.Id.ToString(),
                now,
                "succeeded",
                details,
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));

        await SaveChangesAsync(cancellationToken);
        var view = await _custodyRepository.GetAssignmentViewAsync(
                assignment.Id,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Assignment '{assignment.Id}' was not readable after it was saved.");
        return WithOverdueState(
            view,
            await GetSchoolTodayAsync(cancellationToken));
    }

    public async Task<PageResult<OverdueLoanView>> SearchOverdueAsync(
        Guid? personId,
        Guid? departmentId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (personId == Guid.Empty || departmentId == Guid.Empty)
        {
            throw new RequestValidationException(
                "invalid_holder",
                "Holder identifiers cannot be empty.");
        }

        if (personId.HasValue && departmentId.HasValue)
        {
            throw new RequestValidationException(
                "invalid_holder",
                "Filter by a person or department, not both.");
        }

        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        return await _custodyRepository.SearchOverdueLoansAsync(
            await GetSchoolTodayAsync(cancellationToken),
            personId,
            departmentId,
            normalizedPage.Page,
            normalizedPage.PageSize,
            cancellationToken);
    }

    private async Task<AssignmentTarget> ResolveTargetAsync(
        Guid? personId,
        Guid? departmentId,
        CancellationToken cancellationToken)
    {
        var hasPerson = personId.HasValue && personId.Value != Guid.Empty;
        var hasDepartment = departmentId.HasValue && departmentId.Value != Guid.Empty;
        if (hasPerson == hasDepartment)
        {
            throw new RequestValidationException(
                "invalid_assignment_target",
                "Supply exactly one personId or departmentId.",
                new Dictionary<string, string[]>
                {
                    ["target"] = ["Supply exactly one personId or departmentId."],
                });
        }

        if (hasPerson)
        {
            var person = await _custodyRepository.GetPersonAsync(
                    personId!.Value,
                    cancellationToken)
                ?? throw new ResourceNotFoundException("Person", personId.Value);
            if (!person.IsActive)
            {
                throw new RequestValidationException(
                    "inactive_assignment_target",
                    "The selected person is inactive.");
            }

            if (await _custodyRepository.PersonHasBlockingLeaverCaseAsync(
                    person.Id,
                    cancellationToken))
            {
                throw new ResourceConflictException(
                    "assignment_target_is_leaver",
                    "Assets cannot be assigned to a person with an open or completed leaver case.");
            }

            return new AssignmentTarget(
                person.Id,
                null,
                person.DisplayName,
                person.ExternalId);
        }

        var department = await _custodyRepository.GetDepartmentAsync(
                departmentId!.Value,
                cancellationToken)
            ?? throw new ResourceNotFoundException("Department", departmentId.Value);
        if (!department.IsActive)
        {
            throw new RequestValidationException(
                "inactive_assignment_target",
                "The selected department is inactive.");
        }

        return new AssignmentTarget(
            null,
            department.Id,
            department.Name,
            department.Code);
    }

    private async Task<AssignmentWorkflowResult> BuildWorkflowResultAsync(
        Guid assignmentId,
        Asset asset,
        CancellationToken cancellationToken)
    {
        var assignment = await _custodyRepository.GetAssignmentViewAsync(
                assignmentId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Assignment '{assignmentId}' was not readable after it was saved.");
        var assetView = await _inventoryRepository.GetAssetViewAsync(
                asset.Id,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Asset '{asset.Id}' was not readable after it was saved.");

        var schoolToday = await GetSchoolTodayAsync(cancellationToken);
        return new AssignmentWorkflowResult(
            WithOverdueState(assignment, schoolToday),
            asset.Status,
            asset.Condition,
            asset.LocationId,
            assetView.LocationCode,
            assetView.LocationName,
            asset.Version);
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _inventoryRepository.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new ResourceConflictException(
                exception.Code == "data_conflict"
                    ? "assignment_conflict"
                    : exception.Code,
                exception.Code == "data_conflict"
                    ? "The asset already has a current assignment or the custody data changed."
                    : exception.Message,
                exception);
        }
    }

    private static InventoryException ToApplicationException(DomainRuleException exception) =>
        exception.Code is
            "concurrency_conflict" or
            "assignment_not_allowed" or
            "assignment_not_active" or
            "assignment_already_closed"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);

    private async Task<PageResult<AssetAssignmentView>> WithOverdueStateAsync(
        PageResult<AssetAssignmentView> page,
        CancellationToken cancellationToken)
    {
        var schoolToday = await GetSchoolTodayAsync(cancellationToken);
        return page with
        {
            Items = page.Items
                .Select(item => WithOverdueState(item, schoolToday))
                .ToArray(),
        };
    }

    private static AssetAssignmentView WithOverdueState(
        AssetAssignmentView assignment,
        DateOnly schoolToday) =>
        assignment with
        {
            IsOverdue =
                assignment.ReturnedAtUtc is null &&
                assignment.ExpectedReturnOn.HasValue &&
                assignment.ExpectedReturnOn.Value < schoolToday,
        };

    private async Task<DateOnly> GetSchoolTodayAsync(
        CancellationToken cancellationToken)
    {
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        return SchoolBusinessDate.FromUtc(_clock.UtcNow, settings.TimeZone);
    }

    private sealed record AssignmentTarget(
        Guid? PersonId,
        Guid? DepartmentId,
        string DisplayName,
        string? Reference);
}
