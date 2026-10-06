using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Inventory.Domain.Common;

namespace Inventory.Application.SavedSearches;

public sealed class SavedSearchService
{
    public const int MaximumSavedSearchesPerUser = 50;

    private static readonly JsonSerializerOptions FilterJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter(),
            },
        };

    private readonly ISavedSearchRepository _repository;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;

    public SavedSearchService(
        ISavedSearchRepository repository,
        IClock clock,
        ICurrentUserContext currentUser)
    {
        _repository = repository;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<PageResult<SavedAssetSearchView>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        var result = await _repository.ListAsync(
            actor.UserId,
            normalizedPage.Page,
            normalizedPage.PageSize,
            cancellationToken);
        return new PageResult<SavedAssetSearchView>(
            result.Items.Select(ToView).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<SavedAssetSearchView> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        var record = await _repository.GetRecordAsync(
                actor.UserId,
                id,
                cancellationToken)
            ?? throw new ResourceNotFoundException("Saved asset search", id);
        return ToView(record);
    }

    public async Task<SavedAssetSearchView?> GetDefaultAsync(
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        var record = await _repository.GetDefaultRecordAsync(
            actor.UserId,
            cancellationToken);
        return record is null ? null : ToView(record);
    }

    public async Task<SavedAssetSearchView> CreateAsync(
        CreateSavedAssetSearchCommand command,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        if (await _repository.CountActiveAsync(actor.UserId, cancellationToken) >=
            MaximumSavedSearchesPerUser)
        {
            throw new ResourceConflictException(
                "saved_search_limit_reached",
                $"A user can keep at most {MaximumSavedSearchesPerUser} saved searches.");
        }

        var name = ValidateName(command.Name);
        await EnsureNameAvailableAsync(
            actor.UserId,
            name,
            excludingId: null,
            cancellationToken);
        var filter = SavedAssetFilterValidator.Validate(command.Filter);
        SavedAssetSearch savedSearch;
        try
        {
            savedSearch = SavedAssetSearch.Create(
                actor.UserId,
                name,
                command.SchemaVersion,
                JsonSerializer.Serialize(filter, FilterJsonOptions),
                _clock.UtcNow);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _repository.Add(savedSearch);
        AddAudit(actor, "saved_search.created", savedSearch);
        await SaveChangesAsync(cancellationToken);
        return await GetAsync(savedSearch.Id, cancellationToken);
    }

    public async Task<SavedAssetSearchView> UpdateAsync(
        Guid id,
        UpdateSavedAssetSearchCommand command,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        var savedSearch = await GetEntityAsync(actor.UserId, id, cancellationToken);
        var name = ValidateName(command.Name);
        await EnsureNameAvailableAsync(
            actor.UserId,
            name,
            id,
            cancellationToken);
        var filter = SavedAssetFilterValidator.Validate(command.Filter);
        try
        {
            savedSearch.Update(
                name,
                command.SchemaVersion,
                JsonSerializer.Serialize(filter, FilterJsonOptions),
                command.Version,
                _clock.UtcNow);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        AddAudit(actor, "saved_search.updated", savedSearch);
        await SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<SavedAssetSearchView> SetDefaultAsync(
        Guid id,
        ChangeSavedAssetSearchCommand command,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        await using var transaction =
            await _repository.BeginTransactionAsync(cancellationToken);
        try
        {
            var target = await GetEntityAsync(
                actor.UserId,
                id,
                cancellationToken);
            var current = await _repository.GetDefaultAsync(
                actor.UserId,
                cancellationToken);
            var now = _clock.UtcNow;

            try
            {
                if (current is not null && current.Id != target.Id)
                {
                    current.SetDefault(
                        false,
                        current.Version,
                        now);
                    await SaveChangesAsync(cancellationToken);
                }

                target.SetDefault(
                    true,
                    command.Version,
                    now);
            }
            catch (DomainRuleException exception)
            {
                throw ToApplicationException(exception);
            }

            AddAudit(actor, "saved_search.default.set", target);
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(id, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ArchiveAsync(
        Guid id,
        ChangeSavedAssetSearchCommand command,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        var savedSearch = await GetEntityAsync(actor.UserId, id, cancellationToken);
        try
        {
            savedSearch.Archive(command.Version, _clock.UtcNow);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        AddAudit(actor, "saved_search.archived", savedSearch);
        await SaveChangesAsync(cancellationToken);
    }

    private async Task<SavedAssetSearch> GetEntityAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) =>
        await _repository.GetAsync(userId, id, cancellationToken)
        ?? throw new ResourceNotFoundException("Saved asset search", id);

    private async Task EnsureNameAvailableAsync(
        Guid userId,
        string name,
        Guid? excludingId,
        CancellationToken cancellationToken)
    {
        if (await _repository.NameExistsAsync(
                userId,
                name.ToUpperInvariant(),
                excludingId,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "duplicate_saved_search_name",
                $"A saved search named '{name}' already exists.");
        }
    }

    private static string ValidateName(string name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 100)
        {
            throw FilterValidation(
                "invalid_saved_search_name",
                "Saved search name must contain 1 to 100 characters.",
                "name");
        }

        return normalized;
    }

    private static SavedAssetSearchView ToView(SavedAssetSearchRecord record)
    {
        SavedAssetFilter filter;
        try
        {
            filter = JsonSerializer.Deserialize<SavedAssetFilter>(
                    record.FilterJson,
                    FilterJsonOptions)
                ?? throw new JsonException("The saved filter was null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Saved search '{record.Id}' contains an invalid filter payload.",
                exception);
        }

        return new SavedAssetSearchView(
            record.Id,
            record.Name,
            record.SchemaVersion,
            filter,
            record.IsDefault,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);
    }

    private void AddAudit(
        CurrentActor actor,
        string action,
        SavedAssetSearch savedSearch)
    {
        _repository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                action,
                "saved_asset_search",
                savedSearch.Id.ToString(),
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        savedSearch.Name,
                        savedSearch.SchemaVersion,
                        savedSearch.IsDefault,
                    },
                    FilterJsonOptions),
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

    private static RequestValidationException FilterValidation(
        string code,
        string message,
        string field) =>
        new(
            code,
            message,
            new Dictionary<string, string[]>
            {
                [field] = [message],
            });

    private static InventoryException ToApplicationException(DomainRuleException exception) =>
        exception.Code is "concurrency_conflict" or "saved_search_archived"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);
}
