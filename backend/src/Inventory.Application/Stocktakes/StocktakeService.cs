using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Auditing;
using Inventory.Domain.Common;
using Inventory.Domain.Stocktakes;

namespace Inventory.Application.Stocktakes;

public sealed class StocktakeService
{
    public const int MaximumExpectedAssetsPerSession = 50_000;
    public const int MaximumInProgressSessions = 10;
    public const long EstimatedBytesPerExpectedAsset = 4_096;

    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IStocktakeRepository _repository;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDatabaseStorageWriteGuard _storageWriteGuard;

    public StocktakeService(
        IStocktakeRepository repository,
        IClock clock,
        ICurrentUserContext currentUser,
        IDatabaseStorageWriteGuard storageWriteGuard)
    {
        _repository = repository;
        _clock = clock;
        _currentUser = currentUser;
        _storageWriteGuard = storageWriteGuard;
    }

    public async Task<PageResult<StocktakeSessionListItem>> SearchAsync(
        StocktakeSessionSearch search,
        CancellationToken cancellationToken)
    {
        if (search.Status.HasValue && !Enum.IsDefined(search.Status.Value))
        {
            throw new RequestValidationException(
                "invalid_stocktake_status",
                "The stocktake status is not supported.");
        }

        var page = new PageRequest(search.Page, search.PageSize).Normalize();
        return await _repository.SearchSessionsAsync(
            search with { Page = page.Page, PageSize = page.PageSize },
            cancellationToken);
    }

    public async Task<StocktakeSessionView> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        return await BuildViewAsync(session, cancellationToken);
    }

    public async Task<StocktakeSessionView> StartAsync(
        StartStocktakeCommand command,
        CancellationToken cancellationToken)
    {
        ValidateScopeCommand(command);
        await ValidateScopeReferenceAsync(command, cancellationToken);

        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        StocktakeSession session;
        try
        {
            session = StocktakeSession.Start(
                command.Name,
                command.Scope,
                command.LocationId,
                command.CategoryId,
                now,
                actor.UserId,
                actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        await using var transaction =
            await _repository.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await _repository.HasMatchingInProgressSessionAsync(
                    session.Name.ToUpperInvariant(),
                    session.Scope,
                    session.ScopeLocationId,
                    session.ScopeCategoryId,
                    cancellationToken))
            {
                throw new ResourceConflictException(
                    "stocktake_duplicate_active",
                    "A matching in-progress stocktake already exists.");
            }

            var inProgressCount = await _repository.CountInProgressSessionsAsync(
                cancellationToken);
            if (inProgressCount >= MaximumInProgressSessions)
            {
                throw new ResourceConflictException(
                    "stocktake_active_limit_reached",
                    $"At most {MaximumInProgressSessions} stocktakes can be " +
                    "in progress at once.");
            }

            var sources = await _repository.ListSnapshotSourcesAsync(
                command.Scope,
                command.LocationId,
                command.CategoryId,
                MaximumExpectedAssetsPerSession + 1,
                cancellationToken);
            if (sources.Count > MaximumExpectedAssetsPerSession)
            {
                throw new ResourceConflictException(
                    "stocktake_scope_too_large",
                    $"A stocktake session can snapshot at most " +
                    $"{MaximumExpectedAssetsPerSession} assets. Use a category or " +
                    "location scope to split this stocktake.");
            }

            _storageWriteGuard.EnsureCapacity(
                sources.Count * EstimatedBytesPerExpectedAsset);

            var expectedAssets = sources
                .Select(
                    source => StocktakeExpectedAsset.Capture(
                        session.Id,
                        source.AssetId,
                        source.AssetTag,
                        source.DeviceName,
                        source.CategoryId,
                        source.CategoryCode,
                        source.CategoryName,
                        source.Status,
                        source.Condition,
                        source.LocationId,
                        source.LocationCode,
                        source.LocationName))
                .ToArray();

            _repository.AddSession(session);
            _repository.AddExpectedAssets(expectedAssets);
            _repository.AddAudit(
                CreateAudit(
                    actor,
                    now,
                    "stocktake.started",
                    session.Id,
                    new
                    {
                        session.Name,
                        session.Scope,
                        session.ScopeLocationId,
                        session.ScopeCategoryId,
                        expectedAssetCount = expectedAssets.Length,
                    }));
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await BuildViewAsync(session, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<StocktakeObservationResult> RecordObservationAsync(
        Guid sessionId,
        RecordStocktakeObservationCommand command,
        CancellationToken cancellationToken)
    {
        ValidateAssetSelector(command.AssetId, command.AssetTag);
        if (command.ObservedLocationId == Guid.Empty)
        {
            throw Validation(
                "observed_location_required",
                "observedLocationId is required.",
                "observedLocationId");
        }

        if (!command.ObservedCondition.HasValue ||
            !Enum.IsDefined(command.ObservedCondition.Value))
        {
            throw Validation(
                "invalid_asset_condition",
                "The observed condition is not supported.",
                "observedCondition");
        }

        var session = await GetSessionAsync(sessionId, cancellationToken);
        var assetTag = string.IsNullOrWhiteSpace(command.AssetTag)
            ? null
            : command.AssetTag.Trim().ToUpperInvariant();
        var asset = await _repository.FindAssetAsync(
                command.AssetId,
                assetTag,
                cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Asset",
                command.AssetId?.ToString() ?? command.AssetTag!);
        var location = await _repository.GetLocationAsync(
                command.ObservedLocationId,
                cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Location",
                command.ObservedLocationId);
        if (!location.IsActive)
        {
            throw Validation(
                "inactive_location",
                "The observed location is inactive.",
                "observedLocationId");
        }

        var existing = await _repository.GetObservationAsync(
            sessionId,
            asset.AssetId,
            cancellationToken);
        var previousLocationId = existing?.ObservedLocationId;
        var previousCondition = existing?.ObservedCondition;
        var previousNoteHash = HashNote(existing?.Note);
        var wasRescan = existing is not null;
        var observationChanged = existing is null ||
                                 !existing.Matches(
                                     command.ObservedLocationId,
                                     command.ObservedCondition.Value,
                                     command.Note);
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;

        try
        {
            session.RegisterObservation(command.Version, now, observationChanged);
            if (observationChanged)
            {
                if (existing is null)
                {
                    existing = StocktakeObservation.Record(
                        session.Id,
                        asset.AssetId,
                        asset.AssetTag,
                        asset.DeviceName,
                        asset.CategoryId,
                        asset.CategoryCode,
                        asset.CategoryName,
                        location.Id,
                        location.Code,
                        location.Name,
                        command.ObservedCondition.Value,
                        command.Note,
                        now,
                        actor.UserId,
                        actor.DisplayName);
                    _repository.AddObservation(existing);
                }
                else
                {
                    existing.Update(
                        location.Id,
                        location.Code,
                        location.Name,
                        command.ObservedCondition.Value,
                        command.Note,
                        now,
                        actor.UserId,
                        actor.DisplayName);
                }
            }
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        if (observationChanged)
        {
            _repository.AddAudit(
                CreateAudit(
                    actor,
                    now,
                    "stocktake.asset.observed",
                    session.Id,
                    new
                    {
                        asset.AssetId,
                        asset.AssetTag,
                        observedLocationId = location.Id,
                        observedCondition = command.ObservedCondition.Value,
                        wasRescan,
                        before = wasRescan
                            ? new
                            {
                                locationId = previousLocationId,
                                condition = previousCondition,
                                noteHash = previousNoteHash,
                            }
                            : null,
                        after = new
                        {
                            locationId = existing?.ObservedLocationId,
                            condition = existing?.ObservedCondition,
                            noteHash = HashNote(existing?.Note),
                        },
                    }));
            await SaveChangesAsync(cancellationToken);
        }

        var item = await _repository.GetItemAsync(
                session.Id,
                asset.AssetId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Stocktake item '{asset.AssetId}' was not readable after it was saved.");
        return new StocktakeObservationResult(
            await BuildViewAsync(session, cancellationToken),
            item,
            observationChanged);
    }

    public async Task<PageResult<StocktakeItemView>> GetItemsAsync(
        Guid sessionId,
        StocktakeItemSearch search,
        CancellationToken cancellationToken)
    {
        _ = await GetSessionAsync(sessionId, cancellationToken);
        if (!Enum.IsDefined(search.Filter))
        {
            throw new RequestValidationException(
                "invalid_stocktake_item_filter",
                "The stocktake item filter is not supported.");
        }

        var page = new PageRequest(search.Page, search.PageSize).Normalize();
        return await _repository.GetItemsAsync(
            sessionId,
            search with { Page = page.Page, PageSize = page.PageSize },
            cancellationToken);
    }

    public async Task<StocktakeSessionView> CompleteAsync(
        Guid sessionId,
        Guid version,
        CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        try
        {
            session.Complete(version, now, actor.UserId, actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        var progress = await _repository.GetProgressAsync(sessionId, cancellationToken);
        _repository.AddAudit(
            CreateAudit(
                actor,
                now,
                "stocktake.completed",
                session.Id,
                progress));
        await SaveChangesAsync(cancellationToken);
        return await BuildViewAsync(session, cancellationToken);
    }

    public async Task<StocktakeSessionView> CancelAsync(
        Guid sessionId,
        Guid version,
        string reason,
        CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        try
        {
            session.Cancel(reason, version, now, actor.UserId, actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _repository.AddAudit(
            CreateAudit(
                actor,
                now,
                "stocktake.cancelled",
                session.Id,
                new { reason = session.CancellationReason }));
        await SaveChangesAsync(cancellationToken);
        return await BuildViewAsync(session, cancellationToken);
    }

    private async Task<StocktakeSessionView> BuildViewAsync(
        StocktakeSession session,
        CancellationToken cancellationToken)
    {
        StocktakeReferenceView? location = null;
        StocktakeReferenceView? category = null;
        if (session.ScopeLocationId.HasValue)
        {
            location = await _repository.GetLocationAsync(
                session.ScopeLocationId.Value,
                cancellationToken);
        }

        if (session.ScopeCategoryId.HasValue)
        {
            category = await _repository.GetCategoryAsync(
                session.ScopeCategoryId.Value,
                cancellationToken);
        }

        var progress = await _repository.GetProgressAsync(session.Id, cancellationToken);
        return new StocktakeSessionView(
            session.Id,
            session.Name,
            session.Scope,
            location,
            category,
            session.Status,
            session.StartedAtUtc,
            session.StartedByUserId,
            session.StartedByDisplayName,
            session.CompletedAtUtc,
            session.CompletedByUserId,
            session.CompletedByDisplayName,
            session.CancelledAtUtc,
            session.CancelledByUserId,
            session.CancelledByDisplayName,
            session.CancellationReason,
            progress,
            session.Version);
    }

    private async Task<StocktakeSession> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        await _repository.GetSessionAsync(sessionId, cancellationToken)
        ?? throw new ResourceNotFoundException("Stocktake session", sessionId);

    private async Task ValidateScopeReferenceAsync(
        StartStocktakeCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Scope == StocktakeScope.Location)
        {
            var location = await _repository.GetLocationAsync(
                    command.LocationId!.Value,
                    cancellationToken)
                ?? throw new ResourceNotFoundException("Location", command.LocationId.Value);
            if (!location.IsActive)
            {
                throw Validation(
                    "inactive_location",
                    "The selected stocktake location is inactive.",
                    "locationId");
            }
        }

        if (command.Scope == StocktakeScope.Category)
        {
            var category = await _repository.GetCategoryAsync(
                    command.CategoryId!.Value,
                    cancellationToken)
                ?? throw new ResourceNotFoundException("Category", command.CategoryId.Value);
            if (!category.IsActive)
            {
                throw Validation(
                    "inactive_category",
                    "The selected stocktake category is inactive.",
                    "categoryId");
            }
        }
    }

    private static void ValidateScopeCommand(StartStocktakeCommand command)
    {
        if (!Enum.IsDefined(command.Scope))
        {
            throw Validation(
                "invalid_stocktake_scope",
                "The stocktake scope is not supported.",
                "scope");
        }

        var valid = command.Scope switch
        {
            StocktakeScope.All =>
                !command.LocationId.HasValue && !command.CategoryId.HasValue,
            StocktakeScope.Location =>
                command.LocationId is { } locationId &&
                locationId != Guid.Empty &&
                !command.CategoryId.HasValue,
            StocktakeScope.Category =>
                command.CategoryId is { } categoryId &&
                categoryId != Guid.Empty &&
                !command.LocationId.HasValue,
            _ => false,
        };
        if (!valid)
        {
            throw Validation(
                "invalid_stocktake_scope",
                "Supply only the identifier required by the selected stocktake scope.",
                "scope");
        }
    }

    private static void ValidateAssetSelector(Guid? assetId, string? assetTag)
    {
        var hasId = assetId.HasValue && assetId.Value != Guid.Empty;
        var hasTag = !string.IsNullOrWhiteSpace(assetTag);
        if (hasId == hasTag)
        {
            throw Validation(
                "invalid_asset_selector",
                "Supply exactly one assetId or assetTag.",
                "asset");
        }

        if (assetTag?.Trim().Length > 100)
        {
            throw Validation(
                "maximum_length",
                "assetTag must be 100 characters or fewer.",
                "assetTag");
        }
    }

    private static RequestValidationException Validation(
        string code,
        string message,
        string field) =>
        new(
            code,
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    private static string? HashNote(string? note) =>
        string.IsNullOrWhiteSpace(note)
            ? null
            : Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(note.Trim())))
                .ToLowerInvariant();

    private static AuditLog CreateAudit(
        CurrentActor actor,
        DateTimeOffset occurredAtUtc,
        string action,
        Guid sessionId,
        object details) =>
        AuditLog.Create(
            actor.UserId,
            actor.DisplayName,
            action,
            "stocktake_session",
            sessionId.ToString(),
            occurredAtUtc,
            "succeeded",
            JsonSerializer.Serialize(details, AuditJsonOptions),
            actor.IpAddress,
            actor.Client,
            actor.CorrelationId);

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
        exception.Code is "concurrency_conflict" or "stocktake_not_in_progress"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);
}
