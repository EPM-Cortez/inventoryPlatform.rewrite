using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Assignments;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Domain.Auditing;
using Inventory.Domain.People;

namespace Inventory.Application.People;

public sealed class PeopleService
{
    private readonly ICustodyRepository _repository;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISchoolSettingsProvider _schoolSettingsProvider;

    public PeopleService(
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

    public Task<PageResult<PersonView>> SearchAsync(
        PersonSearch search,
        CancellationToken cancellationToken)
    {
        var page = new PageRequest(search.Page, search.PageSize).Normalize();
        return _repository.SearchPeopleAsync(
            search with
            {
                Query = NormalizeOptional(search.Query),
                Page = page.Page,
                PageSize = page.PageSize,
                Sort = NormalizePersonSort(search.Sort),
            },
            cancellationToken);
    }

    public async Task<PersonView> GetAsync(Guid personId, CancellationToken cancellationToken) =>
        await _repository.GetPersonViewAsync(personId, cancellationToken)
        ?? throw new ResourceNotFoundException("Person", personId);

    public async Task<PersonView> CreateAsync(
        CreatePersonCommand command,
        CancellationToken cancellationToken)
    {
        await EnsureExternalIdAvailableAsync(command.ExternalId, null, cancellationToken);

        Person person;
        try
        {
            person = Person.Create(
                command.ExternalId,
                command.DisplayName,
                command.Type,
                command.Email);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _repository.AddPerson(person);
        var actor = _currentUser.RequireActor();
        _repository.AddAuditLog(AuditLog.Create(
            actor.UserId,
            actor.DisplayName,
            "person.created",
            "person",
            person.Id.ToString(),
            _clock.UtcNow,
            "succeeded",
            JsonSerializer.Serialize(new
            {
                person.ExternalId,
                person.DisplayName,
                person.Type,
                person.IsActive,
            }),
            actor.IpAddress,
            actor.Client,
            actor.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        return await GetAsync(person.Id, cancellationToken);
    }

    public async Task<PersonView> UpdateAsync(
        Guid personId,
        UpdatePersonCommand command,
        CancellationToken cancellationToken)
    {
        var person = await _repository.GetPersonAsync(personId, cancellationToken)
            ?? throw new ResourceNotFoundException("Person", personId);

        await EnsureExternalIdAvailableAsync(
            command.ExternalId,
            personId,
            cancellationToken);

        if (person.IsActive &&
            !command.IsActive &&
            await _repository.PersonHasOpenAssignmentsAsync(personId, cancellationToken))
        {
            throw new ResourceConflictException(
                "holder_has_active_assignments",
                "A person with currently assigned assets cannot be made inactive.");
        }

        var before = new
        {
            person.ExternalId,
            person.DisplayName,
            person.Type,
            person.IsActive,
        };
        try
        {
            person.Update(
                command.ExternalId,
                command.DisplayName,
                command.Type,
                command.Email,
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
            "person.updated",
            "person",
            person.Id.ToString(),
            _clock.UtcNow,
            "succeeded",
            JsonSerializer.Serialize(new
            {
                Before = before,
                After = new
                {
                    person.ExternalId,
                    person.DisplayName,
                    person.Type,
                    person.IsActive,
                },
            }),
            actor.IpAddress,
            actor.Client,
            actor.CorrelationId));
        await SaveChangesAsync(cancellationToken);
        return await GetAsync(person.Id, cancellationToken);
    }

    public async Task<PageResult<HolderAssetView>> GetAssetsAsync(
        Guid personId,
        bool includeHistory,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        _ = await _repository.GetPersonViewAsync(personId, cancellationToken)
            ?? throw new ResourceNotFoundException("Person", personId);
        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        var result = await _repository.GetHolderAssetsAsync(
            personId,
            departmentId: null,
            includeHistory,
            normalizedPage.Page,
            normalizedPage.PageSize,
            cancellationToken);
        return await WithOverdueStateAsync(result, cancellationToken);
    }

    private async Task EnsureExternalIdAvailableAsync(
        string? externalId,
        Guid? excludingPersonId,
        CancellationToken cancellationToken)
    {
        var normalizedExternalId = NormalizeOptionalKey(externalId);
        if (normalizedExternalId is not null &&
            await _repository.PersonExternalIdExistsAsync(
                normalizedExternalId,
                excludingPersonId,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "duplicate_person_external_id",
                $"Person external ID '{externalId!.Trim()}' is already in use.");
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

    private static string NormalizePersonSort(string? sort)
    {
        var normalized = string.IsNullOrWhiteSpace(sort) ? "displayName" : sort.Trim();
        return normalized switch
        {
            "displayName" or "-displayName" or
            "externalId" or "-externalId" or
            "type" or "-type" => normalized,
            _ => throw new RequestValidationException(
                "invalid_sort",
                "Sort must be displayName, externalId, type, or a descending '-' form."),
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptionalKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

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
