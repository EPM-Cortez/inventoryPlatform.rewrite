using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Common;
using Inventory.Domain.Auditing;
using Inventory.Domain.Locations;

namespace Inventory.Application.References;

public sealed class ReferenceDataService
{
    private readonly IInventoryRepository _repository;
    private readonly ILocationGraphMutationRunner _locationGraph;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public ReferenceDataService(
        IInventoryRepository repository,
        ILocationGraphMutationRunner locationGraph,
        ICurrentUserContext currentUser,
        IClock clock)
    {
        _repository = repository;
        _locationGraph = locationGraph;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<PageResult<CategoryView>> ListCategoriesAsync(
        string? query,
        bool includeInactive,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        var normalizedQuery = NormalizeOptional(query);
        var categories = await _repository.ListCategoriesAsync(includeInactive, cancellationToken);
        var matches = categories
            .Where(
                category =>
                    normalizedQuery is null ||
                    category.Code.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                    category.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                    (category.Description?.Contains(
                        normalizedQuery,
                        StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(category => category.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(category => category.Id)
            .ToArray();
        return Page(matches.Select(ToView).ToArray(), normalizedPage);
    }

    public async Task<CategoryView> GetCategoryAsync(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var category = await _repository.GetCategoryAsync(categoryId, cancellationToken)
            ?? throw new ResourceNotFoundException("Category", categoryId);
        return ToView(category);
    }

    public async Task<CategoryView> CreateCategoryAsync(
        CreateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeRequiredCode(command.Code);
        if (await _repository.CategoryCodeExistsAsync(normalizedCode, cancellationToken))
        {
            throw new ResourceConflictException(
                "duplicate_category_code",
                $"Category code '{command.Code.Trim()}' is already in use.");
        }

        AssetCategory category;
        try
        {
            category = AssetCategory.Create(command.Code, command.Name, command.Description);
        }
        catch (DomainRuleException exception)
        {
            throw new RequestValidationException(exception.Code, exception.Message);
        }

        _repository.AddCategory(category);
        AddAudit("category.created", "category", category.Id, null, ToView(category));
        await SaveChangesAsync(cancellationToken);
        return ToView(category);
    }

    public async Task<CategoryView> UpdateCategoryAsync(
        Guid categoryId,
        UpdateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        var category = await _repository.GetCategoryAsync(categoryId, cancellationToken)
            ?? throw new ResourceNotFoundException("Category", categoryId);
        var normalizedCode = NormalizeRequiredCode(command.Code);
        var categories = await _repository.ListCategoriesAsync(
            includeInactive: true,
            cancellationToken);
        if (categories.Any(
                item =>
                    item.Id != categoryId &&
                    string.Equals(
                        item.CodeNormalized,
                        normalizedCode,
                        StringComparison.Ordinal)))
        {
            throw new ResourceConflictException(
                "duplicate_category_code",
                $"Category code '{command.Code.Trim()}' is already in use.");
        }

        var before = ToView(category);
        try
        {
            category.Update(
                command.Code,
                command.Name,
                command.Description,
                command.IsActive,
                command.Version);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        AddAudit("category.updated", "category", category.Id, before, ToView(category));
        await SaveChangesAsync(cancellationToken);
        return ToView(category);
    }

    public async Task<PageResult<LocationView>> ListLocationsAsync(
        string? query,
        bool includeInactive,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        var normalizedQuery = NormalizeOptional(query);
        var locations = await _repository.ListLocationsAsync(includeInactive, cancellationToken);
        var matches = locations
            .Where(
                location =>
                    normalizedQuery is null ||
                    location.Code.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                    location.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                    (location.Type?.Contains(
                        normalizedQuery,
                        StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(location => location.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.Id)
            .ToArray();
        return Page(matches.Select(ToView).ToArray(), normalizedPage);
    }

    public async Task<LocationView> GetLocationAsync(
        Guid locationId,
        CancellationToken cancellationToken)
    {
        var location = await _repository.GetLocationAsync(locationId, cancellationToken)
            ?? throw new ResourceNotFoundException("Location", locationId);
        return ToView(location);
    }

    public Task<LocationView> CreateLocationAsync(
        CreateLocationCommand command,
        CancellationToken cancellationToken) =>
        RunLocationGraphAsync(
            token => CreateLocationCoreAsync(command, token),
            cancellationToken);

    private async Task<LocationView> CreateLocationCoreAsync(
        CreateLocationCommand command,
        CancellationToken cancellationToken)
    {
        var normalizedCode = NormalizeRequiredCode(command.Code);
        if (await _repository.LocationCodeExistsAsync(normalizedCode, cancellationToken))
        {
            throw new ResourceConflictException(
                "duplicate_location_code",
                $"Location code '{command.Code.Trim()}' is already in use.");
        }

        if (command.ParentLocationId.HasValue)
        {
            var parent = await _repository.GetLocationAsync(
                command.ParentLocationId.Value,
                cancellationToken);
            if (parent is null || !parent.IsActive)
            {
                throw new RequestValidationException(
                    "invalid_parent_location",
                    "The parent location does not exist or is inactive.");
            }
        }

        Location location;
        try
        {
            location = Location.Create(
                command.Code,
                command.Name,
                command.Type,
                command.ParentLocationId);
        }
        catch (DomainRuleException exception)
        {
            throw new RequestValidationException(exception.Code, exception.Message);
        }

        _repository.AddLocation(location);
        AddAudit("location.created", "location", location.Id, null, ToView(location));
        await SaveChangesAsync(cancellationToken);
        return ToView(location);
    }

    public Task<LocationView> UpdateLocationAsync(
        Guid locationId,
        UpdateLocationCommand command,
        CancellationToken cancellationToken) =>
        RunLocationGraphAsync(
            token => UpdateLocationCoreAsync(locationId, command, token),
            cancellationToken);

    private async Task<LocationView> UpdateLocationCoreAsync(
        Guid locationId,
        UpdateLocationCommand command,
        CancellationToken cancellationToken)
    {
        var location = await _repository.GetLocationAsync(locationId, cancellationToken)
            ?? throw new ResourceNotFoundException("Location", locationId);
        var normalizedCode = NormalizeRequiredCode(command.Code);
        var locations = await _repository.ListLocationsAsync(
            includeInactive: true,
            cancellationToken);
        if (locations.Any(
                item =>
                    item.Id != locationId &&
                    string.Equals(
                        item.CodeNormalized,
                        normalizedCode,
                        StringComparison.Ordinal)))
        {
            throw new ResourceConflictException(
                "duplicate_location_code",
                $"Location code '{command.Code.Trim()}' is already in use.");
        }

        ValidateLocationLifecycle(locationId, command, locations);

        var before = ToView(location);
        try
        {
            location.Update(
                command.Code,
                command.Name,
                command.Type,
                command.ParentLocationId,
                command.IsActive,
                command.Version);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        AddAudit("location.updated", "location", location.Id, before, ToView(location));
        await SaveChangesAsync(cancellationToken);
        return ToView(location);
    }

    private async Task<LocationView> RunLocationGraphAsync(
        Func<CancellationToken, Task<LocationView>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _locationGraph.RunAsync(operation, cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new ResourceConflictException(
                exception.Code,
                exception.Message,
                exception);
        }
    }

    private void AddAudit(
        string action,
        string targetType,
        Guid targetId,
        object? before,
        object after)
    {
        var actor = _currentUser.RequireActor();
        _repository.AddAuditLog(AuditLog.Create(
            actor.UserId,
            actor.DisplayName,
            action,
            targetType,
            targetId.ToString(),
            _clock.UtcNow,
            "succeeded",
            JsonSerializer.Serialize(new { Before = before, After = after }),
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
            throw new ResourceConflictException(exception.Code, exception.Message, exception);
        }
    }

    private static CategoryView ToView(AssetCategory category) =>
        new(
            category.Id,
            category.Code,
            category.Name,
            category.Description,
            category.IsActive,
            category.Version);

    private static LocationView ToView(Location location) =>
        new(
            location.Id,
            location.Code,
            location.Name,
            location.Type,
            location.ParentLocationId,
            location.IsActive,
            location.Version);

    private static string NormalizeRequiredCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new RequestValidationException("required", "Code is required.");
        }

        return value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PageResult<T> Page<T>(IReadOnlyList<T> items, PageRequest page)
    {
        var skip = ((long)page.Page - 1) * page.PageSize;
        var pageItems = skip >= items.Count
            ? []
            : items.Skip((int)skip).Take(page.PageSize).ToArray();
        return new PageResult<T>(pageItems, page.Page, page.PageSize, items.Count);
    }

    private static void ValidateLocationLifecycle(
        Guid locationId,
        UpdateLocationCommand command,
        IReadOnlyList<Location> locations)
    {
        if (!command.IsActive &&
            locations.Any(
                location =>
                    location.IsActive &&
                    location.ParentLocationId == locationId))
        {
            throw new RequestValidationException(
                "location_has_active_children",
                "Deactivate or move active child locations before deactivating this location.");
        }

        if (!command.ParentLocationId.HasValue)
        {
            return;
        }

        var byId = locations.ToDictionary(location => location.Id);
        if (!byId.TryGetValue(command.ParentLocationId.Value, out var parent) ||
            (command.IsActive && !parent.IsActive))
        {
            throw new RequestValidationException(
                "invalid_parent_location",
                "The parent location does not exist or is inactive.");
        }

        var visited = new HashSet<Guid>();
        Location? current = parent;
        while (current is not null)
        {
            if (current.Id == locationId)
            {
                throw new RequestValidationException(
                    "location_cycle",
                    "The selected parent would create a location cycle.");
            }

            if (!visited.Add(current.Id) ||
                !current.ParentLocationId.HasValue ||
                !byId.TryGetValue(current.ParentLocationId.Value, out current))
            {
                break;
            }
        }
    }

    private static InventoryException ToApplicationException(DomainRuleException exception) =>
        exception.Code == "concurrency_conflict"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);
}
