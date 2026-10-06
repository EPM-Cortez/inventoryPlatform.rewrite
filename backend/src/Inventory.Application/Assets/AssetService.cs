using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.AssetTags;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Common;

namespace Inventory.Application.Assets;

public sealed class AssetService
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IInventoryRepository _repository;
    private readonly ICustodyRepository _custodyRepository;
    private readonly IAssetOperationsRepository _assetOperationsRepository;
    private readonly AssetTagPolicyService _assetTagPolicyService;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;

    public AssetService(
        IInventoryRepository repository,
        ICustodyRepository custodyRepository,
        IAssetOperationsRepository assetOperationsRepository,
        AssetTagPolicyService assetTagPolicyService,
        IClock clock,
        ICurrentUserContext currentUser)
    {
        _repository = repository;
        _custodyRepository = custodyRepository;
        _assetOperationsRepository = assetOperationsRepository;
        _assetTagPolicyService = assetTagPolicyService;
        _clock = clock;
        _currentUser = currentUser;
    }

    public Task<PageResult<AssetListItem>> SearchAsync(
        AssetSearch search,
        CancellationToken cancellationToken)
    {
        var page = new PageRequest(search.Page, search.PageSize).Normalize();
        if (search.CreatedFrom.HasValue &&
            search.CreatedTo.HasValue &&
            search.CreatedTo.Value < search.CreatedFrom.Value)
        {
            throw new RequestValidationException(
                "invalid_created_range",
                "createdTo cannot precede createdFrom.",
                new Dictionary<string, string[]>
                {
                    ["createdTo"] = ["createdTo cannot precede createdFrom."],
                });
        }

        var normalizedSearch = search with
        {
            Query = NormalizeOptional(search.Query),
            AssetTag = NormalizeOptional(search.AssetTag),
            SerialNumber = NormalizeOptional(search.SerialNumber),
            LegacyId = NormalizeOptional(search.LegacyId),
            Page = page.Page,
            PageSize = page.PageSize,
            Sort = NormalizeSort(search.Sort),
        };

        return _repository.SearchAssetsAsync(normalizedSearch, cancellationToken);
    }

    public async Task<AssetView> GetAsync(Guid assetId, CancellationToken cancellationToken) =>
        await _repository.GetAssetViewAsync(assetId, cancellationToken)
        ?? throw new ResourceNotFoundException("Asset", assetId);

    public async Task<AssetView> GetByTagAsync(
        string assetTag,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeRequiredKey(assetTag, "assetTag");
        return await _repository.GetAssetViewByTagAsync(normalized, cancellationToken)
            ?? throw new ResourceNotFoundException("Asset", assetTag);
    }

    public async Task<AssetView> CreateAsync(
        CreateAssetCommand command,
        CancellationToken cancellationToken)
    {
        await ValidateReferencesAsync(command.CategoryId, command.LocationId, cancellationToken);
        if (!command.GenerateAssetTag)
        {
            return await CreateResolvedAsync(command, cancellationToken);
        }

        await using var transaction =
            await _assetOperationsRepository.BeginTransactionAsync(
                cancellationToken);
        try
        {
            var created = await CreateResolvedAsync(command, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return created;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<AssetView> CreateResolvedAsync(
        CreateAssetCommand command,
        CancellationToken cancellationToken)
    {
        var resolvedAssetTag =
            await _assetTagPolicyService.ResolveForCreateAsync(
                command.AssetTag,
                command.GenerateAssetTag,
                cancellationToken);
        command = command with { AssetTag = resolvedAssetTag };
        await EnsureIdentifiersAvailableAsync(
            resolvedAssetTag,
            command.SerialNumber,
            excludingAssetId: null,
            cancellationToken);

        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        Asset asset;

        try
        {
            asset = Asset.Create(ToDetails(command), now);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _repository.AddAsset(asset);
        _repository.AddAssetEvent(
            AssetEvent.Create(
                asset.Id,
                AssetEventType.AssetCreated,
                now,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                $"Asset {asset.AssetTag} was created.",
                JsonSerializer.Serialize(
                    new
                    {
                        asset.AssetTag,
                        asset.CategoryId,
                        asset.Status,
                        asset.Condition,
                        asset.LocationId,
                    },
                    EventJsonOptions),
                actor.CorrelationId));

        await SaveChangesAsync(cancellationToken);
        return await GetAsync(asset.Id, cancellationToken);
    }

    public async Task<AssetView> UpdateAsync(
        Guid assetId,
        UpdateAssetCommand command,
        CancellationToken cancellationToken)
    {
        var asset = await _repository.GetAssetAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Asset", assetId);

        await ValidateReferencesAsync(command.CategoryId, command.LocationId, cancellationToken);
        await EnsureIdentifiersAvailableAsync(
            command.AssetTag,
            command.SerialNumber,
            assetId,
            cancellationToken);

        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        IReadOnlyDictionary<string, FieldChange> changes;

        try
        {
            changes = asset.Update(ToDetails(command), command.Version, now);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        if (changes.Count == 0)
        {
            return await GetAsync(assetId, cancellationToken);
        }

        AddUpdateEvents(asset, changes, actor, now);
        await SaveChangesAsync(cancellationToken);
        return await GetAsync(asset.Id, cancellationToken);
    }

    public async Task<AssetView> RetireAsync(
        Guid assetId,
        Guid version,
        string reason,
        CancellationToken cancellationToken)
    {
        var asset = await _repository.GetAssetAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Asset", assetId);
        var assignment = await _custodyRepository.GetOpenAssignmentAsync(
            assetId,
            cancellationToken);
        await EnsureNoLinkedManagedAgentAsync(assetId, cancellationToken);

        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;

        try
        {
            asset.Retire(reason, version, now);
            if (assignment is not null)
            {
                assignment.Close(
                    assignment.Version,
                    now,
                    actor.UserId,
                    actor.DisplayName,
                    asset.Condition,
                    asset.LocationId,
                    "Assignment closed because the asset was retired.");
            }
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        if (assignment is not null)
        {
            _repository.AddAssetEvent(
                AssetEvent.Create(
                    asset.Id,
                    AssetEventType.Returned,
                    now,
                    actor.UserId,
                    actor.DisplayName,
                    AssetEventSource.Api,
                    $"Asset {asset.AssetTag} custody was closed because it was retired.",
                    JsonSerializer.Serialize(
                        new
                        {
                            assignmentId = assignment.Id,
                            targetType = assignment.TargetType,
                            targetId = assignment.PersonId ?? assignment.DepartmentId,
                            targetDisplayName = assignment.TargetDisplayName,
                            condition = assignment.ReturnCondition,
                            locationId = assignment.ReturnLocationId,
                            reason = "asset_retired",
                        },
                        EventJsonOptions),
                    actor.CorrelationId));
        }

        _repository.AddAssetEvent(
            AssetEvent.Create(
                asset.Id,
                AssetEventType.Retired,
                now,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                $"Asset {asset.AssetTag} was retired.",
                JsonSerializer.Serialize(new { reason = asset.RetirementReason }, EventJsonOptions),
                actor.CorrelationId));

        await SaveChangesAsync(cancellationToken);
        return await GetAsync(asset.Id, cancellationToken);
    }

    public async Task<AssetView> DisposeAsync(
        Guid assetId,
        Guid version,
        string reason,
        CancellationToken cancellationToken)
    {
        var asset = await _repository.GetAssetAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Asset", assetId);
        var assignment = await _custodyRepository.GetOpenAssignmentAsync(
            assetId,
            cancellationToken);
        await EnsureNoLinkedManagedAgentAsync(assetId, cancellationToken);

        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;

        try
        {
            asset.Dispose(reason, version, now);
            if (assignment is not null)
            {
                assignment.Close(
                    assignment.Version,
                    now,
                    actor.UserId,
                    actor.DisplayName,
                    asset.Condition,
                    asset.LocationId,
                    "Assignment closed because the asset was disposed.");
            }
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        if (assignment is not null)
        {
            _repository.AddAssetEvent(
                AssetEvent.Create(
                    asset.Id,
                    AssetEventType.Returned,
                    now,
                    actor.UserId,
                    actor.DisplayName,
                    AssetEventSource.Api,
                    $"Asset {asset.AssetTag} custody was closed because it was disposed.",
                    JsonSerializer.Serialize(
                        new
                        {
                            assignmentId = assignment.Id,
                            targetType = assignment.TargetType,
                            targetId = assignment.PersonId ?? assignment.DepartmentId,
                            targetDisplayName = assignment.TargetDisplayName,
                            condition = assignment.ReturnCondition,
                            locationId = assignment.ReturnLocationId,
                            reason = "asset_disposed",
                        },
                        EventJsonOptions),
                    actor.CorrelationId));
        }

        _repository.AddAssetEvent(
            AssetEvent.Create(
                asset.Id,
                AssetEventType.Disposed,
                now,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                $"Asset {asset.AssetTag} was disposed.",
                JsonSerializer.Serialize(new { reason = asset.DisposalReason }, EventJsonOptions),
                actor.CorrelationId));

        await SaveChangesAsync(cancellationToken);
        return await GetAsync(asset.Id, cancellationToken);
    }

    public async Task<AssetView> RestoreAsync(
        Guid assetId,
        Guid version,
        CancellationToken cancellationToken)
    {
        var asset = await _repository.GetAssetAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Asset", assetId);

        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;

        try
        {
            asset.Restore(version, now);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _repository.AddAssetEvent(
            AssetEvent.Create(
                asset.Id,
                AssetEventType.Restored,
                now,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                $"Asset {asset.AssetTag} was restored to stock.",
                "{}",
                actor.CorrelationId));

        await SaveChangesAsync(cancellationToken);
        return await GetAsync(asset.Id, cancellationToken);
    }

    public async Task<PageResult<AssetEventView>> GetHistoryAsync(
        Guid assetId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        _ = await _repository.GetAssetViewAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Asset", assetId);

        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        var events = await _repository.GetAssetHistoryAsync(
            assetId,
            normalizedPage.Page,
            normalizedPage.PageSize,
            cancellationToken);

        return new PageResult<AssetEventView>(
            events.Items.Select(ToView).ToArray(),
            events.Page,
            events.PageSize,
            events.TotalCount);
    }

    public async Task<AssetEventView> AddNoteAsync(
        Guid assetId,
        string note,
        CancellationToken cancellationToken)
    {
        var asset = await _repository.GetAssetAsync(assetId, cancellationToken)
            ?? throw new ResourceNotFoundException("Asset", assetId);

        var normalizedNote = note?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedNote) || normalizedNote.Length > 4000)
        {
            throw new RequestValidationException(
                "invalid_note",
                "A note between 1 and 4000 characters is required.",
                new Dictionary<string, string[]>
                {
                    ["note"] = ["A note between 1 and 4000 characters is required."],
                });
        }

        var actor = _currentUser.RequireActor();
        var assetEvent = AssetEvent.Create(
            asset.Id,
            AssetEventType.NoteAdded,
            _clock.UtcNow,
            actor.UserId,
            actor.DisplayName,
            AssetEventSource.Api,
            normalizedNote,
            JsonSerializer.Serialize(new { note = normalizedNote }, EventJsonOptions),
            actor.CorrelationId);

        _repository.AddAssetEvent(assetEvent);
        await SaveChangesAsync(cancellationToken);
        return ToView(assetEvent);
    }

    private async Task ValidateReferencesAsync(
        Guid categoryId,
        Guid? locationId,
        CancellationToken cancellationToken)
    {
        var category = await _repository.GetCategoryAsync(categoryId, cancellationToken);
        if (category is null || !category.IsActive)
        {
            throw new RequestValidationException(
                "invalid_category",
                "The selected category does not exist or is inactive.",
                new Dictionary<string, string[]>
                {
                    ["categoryId"] = ["The selected category does not exist or is inactive."],
                });
        }

        if (locationId.HasValue)
        {
            var location = await _repository.GetLocationAsync(locationId.Value, cancellationToken);
            if (location is null || !location.IsActive)
            {
                throw new RequestValidationException(
                    "invalid_location",
                    "The selected location does not exist or is inactive.",
                    new Dictionary<string, string[]>
                    {
                        ["locationId"] = ["The selected location does not exist or is inactive."],
                    });
            }
        }
    }

    private async Task EnsureIdentifiersAvailableAsync(
        string assetTag,
        string? serialNumber,
        Guid? excludingAssetId,
        CancellationToken cancellationToken)
    {
        var normalizedTag = NormalizeRequiredKey(assetTag, "assetTag");
        if (await _repository.AssetTagExistsAsync(
                normalizedTag,
                excludingAssetId,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "duplicate_asset_tag",
                $"Asset tag '{assetTag.Trim()}' is already in use.");
        }

        var normalizedSerial = NormalizeOptionalKey(serialNumber);
        if (normalizedSerial is not null &&
            await _repository.SerialNumberExistsAsync(
                normalizedSerial,
                excludingAssetId,
                cancellationToken))
        {
            throw new ResourceConflictException(
                "duplicate_serial_number",
                $"Serial number '{serialNumber!.Trim()}' is already in use.");
        }
    }

    private async Task EnsureNoLinkedManagedAgentAsync(
        Guid assetId,
        CancellationToken cancellationToken)
    {
        if (await _repository.GetLinkedManagedAgentIdAsync(
                assetId,
                cancellationToken) is not null)
        {
            throw new ResourceConflictException(
                "asset_has_linked_agent",
                "Unlink the managed endpoint agent before retiring or disposing this asset.");
        }
    }

    private void AddUpdateEvents(
        Asset asset,
        IReadOnlyDictionary<string, FieldChange> changes,
        CurrentActor actor,
        DateTimeOffset occurredAtUtc)
    {
        var specialisedFields = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(Asset.Status),
            nameof(Asset.Condition),
            nameof(Asset.LocationId),
        };

        foreach (var (field, type, summary) in new[]
                 {
                     (
                         nameof(Asset.Status),
                         AssetEventType.StatusChanged,
                         $"Asset {asset.AssetTag} status changed."),
                     (
                         nameof(Asset.Condition),
                         AssetEventType.ConditionChanged,
                         $"Asset {asset.AssetTag} condition changed."),
                     (
                         nameof(Asset.LocationId),
                         AssetEventType.LocationChanged,
                         $"Asset {asset.AssetTag} location changed."),
                 })
        {
            if (changes.TryGetValue(field, out var change))
            {
                AddChangeEvent(asset.Id, type, summary, field, change, actor, occurredAtUtc);
            }
        }

        var detailChanges = changes
            .Where(change => !specialisedFields.Contains(change.Key))
            .ToDictionary(change => change.Key, change => change.Value, StringComparer.Ordinal);

        if (detailChanges.Count > 0)
        {
            _repository.AddAssetEvent(
                AssetEvent.Create(
                    asset.Id,
                    AssetEventType.AssetDetailsChanged,
                    occurredAtUtc,
                    actor.UserId,
                    actor.DisplayName,
                    AssetEventSource.Api,
                    $"Asset {asset.AssetTag} details changed.",
                    JsonSerializer.Serialize(new { changes = detailChanges }, EventJsonOptions),
                    actor.CorrelationId));
        }
    }

    private void AddChangeEvent(
        Guid assetId,
        AssetEventType type,
        string summary,
        string field,
        FieldChange change,
        CurrentActor actor,
        DateTimeOffset occurredAtUtc)
    {
        _repository.AddAssetEvent(
            AssetEvent.Create(
                assetId,
                type,
                occurredAtUtc,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                summary,
                JsonSerializer.Serialize(new { field, change.From, change.To }, EventJsonOptions),
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

    private static AssetDetails ToDetails(CreateAssetCommand command) =>
        new(
            command.AssetTag
                ?? throw new InvalidOperationException(
                    "The asset tag was not resolved before asset creation."),
            command.CategoryId,
            command.Manufacturer,
            command.Model,
            command.SerialNumber,
            command.Status,
            command.Condition,
            command.LocationId,
            command.PurchaseDate,
            command.PurchaseCost,
            command.Currency,
            command.Supplier,
            command.Notes,
            command.LegacyId,
            command.DeviceName,
            command.Description,
            command.LanMacAddress,
            command.BiosGuid,
            command.Ownership,
            command.OperatingSystem,
            command.OperatingSystemVersion,
            command.Windows11Compliant);

    private static AssetDetails ToDetails(UpdateAssetCommand command) =>
        new(
            command.AssetTag,
            command.CategoryId,
            command.Manufacturer,
            command.Model,
            command.SerialNumber,
            command.Status,
            command.Condition,
            command.LocationId,
            command.PurchaseDate,
            command.PurchaseCost,
            command.Currency,
            command.Supplier,
            command.Notes,
            command.LegacyId,
            command.DeviceName,
            command.Description,
            command.LanMacAddress,
            command.BiosGuid,
            command.Ownership,
            command.OperatingSystem,
            command.OperatingSystemVersion,
            command.Windows11Compliant);

    private static AssetEventView ToView(AssetEvent assetEvent) =>
        new(
            assetEvent.Id,
            assetEvent.Type,
            assetEvent.OccurredAtUtc,
            assetEvent.ActorUserId,
            assetEvent.ActorDisplayName,
            assetEvent.Source,
            assetEvent.Summary,
            assetEvent.DetailsJson,
            assetEvent.CorrelationId);

    private static InventoryException ToApplicationException(DomainRuleException exception) =>
        exception.Code == "concurrency_conflict"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);

    private static string NormalizeRequiredKey(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new RequestValidationException(
                "required",
                $"{fieldName} is required.",
                new Dictionary<string, string[]> { [fieldName] = [$"{fieldName} is required."] });
        }

        return value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptionalKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeSort(string? sort)
    {
        var normalized = string.IsNullOrWhiteSpace(sort) ? "assetTag" : sort.Trim();
        return normalized switch
        {
            "assetTag" or "-assetTag" or
            "updatedAtUtc" or "-updatedAtUtc" or
            "status" or "-status" => normalized,
            _ => throw new RequestValidationException(
                "invalid_sort",
                "Sort must be one of assetTag, updatedAtUtc, status, or the descending '-' form.",
                new Dictionary<string, string[]>
                {
                    ["sort"] =
                    [
                        "Sort must be one of assetTag, updatedAtUtc, status, or the descending '-' form.",
                    ],
                }),
        };
    }
}
