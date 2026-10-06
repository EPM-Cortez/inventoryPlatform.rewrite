using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Auditing;
using Inventory.Domain.Common;
using Inventory.Domain.People;

namespace Inventory.Application.Leavers;

public sealed class LeaverService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly ILeaverRepository _repository;
    private readonly ICustodyRepository _custodyRepository;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISchoolSettingsProvider _schoolSettingsProvider;

    public LeaverService(
        ILeaverRepository repository,
        ICustodyRepository custodyRepository,
        IClock clock,
        ICurrentUserContext currentUser,
        ISchoolSettingsProvider schoolSettingsProvider)
    {
        _repository = repository;
        _custodyRepository = custodyRepository;
        _clock = clock;
        _currentUser = currentUser;
        _schoolSettingsProvider = schoolSettingsProvider;
    }

    public async Task<PageResult<LeaverCaseSummaryView>> SearchAsync(
        LeaverCaseSearch search,
        CancellationToken cancellationToken)
    {
        if (search.Status.HasValue && !Enum.IsDefined(search.Status.Value))
        {
            throw new RequestValidationException(
                "invalid_leaver_status",
                "The leaver status is not supported.",
                new Dictionary<string, string[]>
                {
                    ["status"] = ["The leaver status is not supported."],
                });
        }

        var page = new PageRequest(search.Page, search.PageSize).Normalize();
        return await _repository.SearchAsync(
            search with
            {
                Query = NormalizeOptional(search.Query),
                Page = page.Page,
                PageSize = page.PageSize,
            },
            await GetSchoolTodayAsync(cancellationToken),
            cancellationToken);
    }

    public async Task<PageResult<CustodyExceptionView>> SearchExceptionsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        return await _repository.SearchExceptionsAsync(
            await GetSchoolTodayAsync(cancellationToken),
            normalizedPage.Page,
            normalizedPage.PageSize,
            cancellationToken);
    }

    public async Task<LeaverCaseDetailView> GetAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await _repository.GetViewAsync(
            id,
            await GetSchoolTodayAsync(cancellationToken),
            cancellationToken)
        ?? throw new ResourceNotFoundException("Leaver case", id);

    public async Task<PageResult<LeaverCustodyAssetView>> GetCurrentAssetsAsync(
        Guid id,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var leaverCase = await GetEntityAsync(id, cancellationToken);
        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        return await _repository.GetCurrentAssetsAsync(
            leaverCase.PersonId,
            await GetSchoolTodayAsync(cancellationToken),
            normalizedPage.Page,
            normalizedPage.PageSize,
            cancellationToken);
    }

    public async Task<LeaverCaseDetailView> CreateAsync(
        CreateLeaverCaseCommand command,
        CancellationToken cancellationToken)
    {
        _ = await _custodyRepository.GetPersonViewAsync(
                command.PersonId,
                cancellationToken)
            ?? throw new ResourceNotFoundException("Person", command.PersonId);
        if (await _repository.HasOpenCaseAsync(
                command.PersonId,
                excludingCaseId: null,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "open_leaver_case_exists",
                "This person already has an open leaver case.");
        }

        var actor = _currentUser.RequireActor();
        LeaverCase leaverCase;
        try
        {
            leaverCase = LeaverCase.Create(
                command.PersonId,
                command.LeavingOn,
                command.Notes,
                _clock.UtcNow,
                actor.UserId,
                actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _repository.Add(leaverCase);
        AddAudit(
            actor,
            "leaver.created",
            leaverCase,
            new
            {
                leaverCase.PersonId,
                leaverCase.LeavingOn,
            });
        await SaveChangesAsync(cancellationToken);
        return await GetAsync(leaverCase.Id, cancellationToken);
    }

    public async Task<LeaverCaseDetailView> UpdateAsync(
        Guid id,
        UpdateLeaverCaseCommand command,
        CancellationToken cancellationToken)
    {
        var leaverCase = await GetEntityAsync(id, cancellationToken);
        try
        {
            leaverCase.Update(command.LeavingOn, command.Notes, command.Version);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        var actor = _currentUser.RequireActor();
        AddAudit(
            actor,
            "leaver.updated",
            leaverCase,
            new
            {
                leaverCase.PersonId,
                leaverCase.LeavingOn,
            });
        await SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<LeaverCaseDetailView> CompleteAsync(
        Guid id,
        ChangeLeaverCaseStatusCommand command,
        CancellationToken cancellationToken)
    {
        var leaverCase = await GetEntityAsync(id, cancellationToken);
        if (await _custodyRepository.PersonHasOpenAssignmentsAsync(
                leaverCase.PersonId,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "leaver_has_open_custody",
                "The leaver case cannot be completed while the person still has assets.");
        }

        var actor = _currentUser.RequireActor();
        try
        {
            leaverCase.Complete(
                command.Version,
                _clock.UtcNow,
                actor.UserId,
                actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        AddAudit(
            actor,
            "leaver.completed",
            leaverCase,
            new { leaverCase.PersonId });
        await SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<LeaverCaseDetailView> CancelAsync(
        Guid id,
        ChangeLeaverCaseStatusCommand command,
        CancellationToken cancellationToken)
    {
        var leaverCase = await GetEntityAsync(id, cancellationToken);
        var actor = _currentUser.RequireActor();
        try
        {
            leaverCase.Cancel(
                command.Version,
                _clock.UtcNow,
                actor.UserId,
                actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        AddAudit(
            actor,
            "leaver.cancelled",
            leaverCase,
            new { leaverCase.PersonId });
        await SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<LeaverCaseDetailView> MarkRehiredAsync(
        Guid id,
        ChangeLeaverCaseStatusCommand command,
        CancellationToken cancellationToken)
    {
        var leaverCase = await GetEntityAsync(id, cancellationToken);
        try
        {
            leaverCase.MarkRehired(command.Version);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        var actor = _currentUser.RequireActor();
        AddAudit(
            actor,
            "leaver.rehired",
            leaverCase,
            new { leaverCase.PersonId });
        await SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<LeaverCase> GetEntityAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        await _repository.GetAsync(id, cancellationToken)
        ?? throw new ResourceNotFoundException("Leaver case", id);

    private async Task<DateOnly> GetSchoolTodayAsync(
        CancellationToken cancellationToken)
    {
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        return SchoolBusinessDate.FromUtc(_clock.UtcNow, settings.TimeZone);
    }

    private void AddAudit(
        CurrentActor actor,
        string action,
        LeaverCase leaverCase,
        object details)
    {
        _repository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                action,
                "leaver_case",
                leaverCase.Id.ToString(),
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(details, JsonOptions),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new ResourceConflictException(
                exception.Code,
                exception.Message,
                exception);
        }
    }

    private static InventoryException ToApplicationException(DomainRuleException exception) =>
        exception.Code is
            "concurrency_conflict" or
            "leaver_case_closed" or
            "leaver_case_not_completed"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
