using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Inventory.Domain.BulkChanges;
using Inventory.Domain.Common;

namespace Inventory.Application.BulkChanges;

public sealed class AssetBulkChangeService
{
    public const int MaximumActivePreviewsPerUser = 20;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(30);

    private readonly IAssetOperationsRepository _operationsRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;

    public AssetBulkChangeService(
        IAssetOperationsRepository operationsRepository,
        IInventoryRepository inventoryRepository,
        IClock clock,
        ICurrentUserContext currentUser)
    {
        _operationsRepository = operationsRepository;
        _inventoryRepository = inventoryRepository;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<AssetBulkChangePreviewView> PreviewAsync(
        PreviewAssetBulkChangeCommand command,
        CancellationToken cancellationToken)
    {
        var selections = NormalizeSelections(command.Assets);
        var operation = NormalizeAndValidateOperation(command.Operation);
        var reason = NormalizeReason(command.Reason);
        await ValidateOperationReferencesAsync(operation, cancellationToken);
        var assets = await LoadAndValidateAssetsAsync(
            selections,
            operation,
            cancellationToken);
        var previewItems = assets
            .OrderBy(asset => asset.AssetTag, StringComparer.OrdinalIgnoreCase)
            .Select(asset => BuildPreviewItem(asset, operation))
            .ToArray();
        var itemsJson = JsonSerializer.Serialize(selections, JsonOptions);
        var operationJson = JsonSerializer.Serialize(operation, JsonOptions);
        var fingerprint = CreateFingerprint(operation, selections, reason);
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        var reusable = await _operationsRepository.GetReusableBulkChangeJobAsync(
            actor.UserId,
            fingerprint,
            now,
            cancellationToken);
        if (reusable is not null)
        {
            return ToPreviewView(reusable, previewItems);
        }

        var activePreviewCount =
            await _operationsRepository.CountActiveBulkChangeJobsAsync(
                actor.UserId,
                now,
                cancellationToken);
        if (activePreviewCount >= MaximumActivePreviewsPerUser)
        {
            throw new ResourceConflictException(
                "bulk_preview_limit_reached",
                $"A user can have at most {MaximumActivePreviewsPerUser} " +
                "unexpired bulk previews. Commit one or wait for it to expire.");
        }

        var job = AssetBulkChangeJob.Create(
            operation.Kind,
            operationJson,
            itemsJson,
            reason,
            fingerprint,
            selections.Length,
            actor.UserId,
            actor.DisplayName,
            now,
            now.Add(PreviewLifetime));
        _operationsRepository.Add(job);
        _operationsRepository.AddAuditLog(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "assets.bulk.previewed",
                "asset_bulk_change_job",
                job.Id.ToString(),
                now,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        JobId = job.Id,
                        job.OperationKind,
                        job.AssetCount,
                        job.Reason,
                        job.Fingerprint,
                        job.ExpiresAtUtc,
                    },
                    JsonOptions),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
        await SaveAsync(cancellationToken);
        return ToPreviewView(job, previewItems);
    }

    public async Task<AssetBulkChangeCommitView> CommitAsync(
        CommitAssetBulkChangeCommand command,
        CancellationToken cancellationToken)
    {
        if (command.JobId == Guid.Empty)
        {
            throw Validation(
                "bulk_preview_id_required",
                "jobId",
                "A bulk preview identifier is required.");
        }

        if (command.Version == Guid.Empty)
        {
            throw Validation(
                "bulk_preview_version_required",
                "version",
                "The bulk preview version is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Fingerprint))
        {
            throw Validation(
                "bulk_preview_fingerprint_required",
                "fingerprint",
                "The bulk preview fingerprint is required.");
        }

        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        await using var transaction =
            await _operationsRepository.BeginTransactionAsync(cancellationToken);
        try
        {
            var job = await _operationsRepository.GetBulkChangeJobForUpdateAsync(
                    command.JobId,
                    cancellationToken)
                ?? throw new ResourceNotFoundException(
                    "Asset bulk change preview",
                    command.JobId);
            if (job.CreatedByUserId != actor.UserId)
            {
                throw new ResourceNotFoundException(
                    "Asset bulk change preview",
                    command.JobId);
            }

            var operation = DeserializeOperation(job.OperationJson);
            var selections = DeserializeSelections(job.ItemsJson);
            await ValidateOperationReferencesAsync(operation, cancellationToken);
            var assets = await LoadAndValidateAssetsAsync(
                selections,
                operation,
                cancellationToken);

            try
            {
                job.Commit(command.Version, command.Fingerprint, now);
            }
            catch (DomainRuleException exception)
            {
                throw MapDomainException(exception);
            }

            foreach (var asset in assets)
            {
                ApplyChange(asset, operation, job, actor, now);
            }

            _operationsRepository.AddAuditLog(
                AuditLog.Create(
                    actor.UserId,
                    actor.DisplayName,
                    "assets.bulk.committed",
                    "asset_bulk_change_job",
                    job.Id.ToString(),
                    now,
                    "succeeded",
                    JsonSerializer.Serialize(
                        new
                        {
                            JobId = job.Id,
                            job.OperationKind,
                            job.AssetCount,
                            job.Reason,
                            PreviewedByUserId = job.CreatedByUserId,
                            AssetIds = selections.Select(item => item.AssetId),
                        },
                        JsonOptions),
                    actor.IpAddress,
                    actor.Client,
                    actor.CorrelationId));
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AssetBulkChangeCommitView(
                job.Id,
                job.Status,
                job.AssetCount,
                job.CommittedAtUtc
                    ?? throw new InvalidOperationException(
                        "Committed bulk job has no commit time."),
                assets
                    .OrderBy(asset => asset.AssetTag, StringComparer.OrdinalIgnoreCase)
                    .Select(
                        asset => new AssetBulkChangeCommitItem(
                            asset.Id,
                            asset.AssetTag,
                            asset.Version))
                    .ToArray(),
                job.Version);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<IReadOnlyList<Asset>> LoadAndValidateAssetsAsync(
        AssetBulkChangeSelection[] selections,
        AssetBulkChangeOperation operation,
        CancellationToken cancellationToken)
    {
        var assets = await _operationsRepository.GetAssetsForUpdateAsync(
            selections.Select(selection => selection.AssetId).ToArray(),
            cancellationToken);
        if (assets.Count != selections.Length)
        {
            var found = assets.Select(asset => asset.Id).ToHashSet();
            var missing = selections
                .Where(selection => !found.Contains(selection.AssetId))
                .Select(selection => selection.AssetId)
                .Order()
                .ToArray();
            throw new ResourceConflictException(
                "bulk_asset_missing",
                $"The bulk selection contains missing assets: {string.Join(", ", missing)}.");
        }

        var expectedVersions = selections.ToDictionary(
            selection => selection.AssetId,
            selection => selection.Version);
        var stale = assets
            .Where(asset => asset.Version != expectedVersions[asset.Id])
            .Select(asset => asset.Id)
            .Order()
            .ToArray();
        if (stale.Length > 0)
        {
            throw new ResourceConflictException(
                "bulk_asset_version_conflict",
                "One or more assets changed after the selection was made. Create a new preview.");
        }

        foreach (var asset in assets)
        {
            if (asset.Status is AssetStatus.Retired or AssetStatus.Disposed)
            {
                throw new ResourceConflictException(
                    "bulk_asset_not_mutable",
                    $"Asset '{asset.AssetTag}' is retired or disposed.");
            }

            if (asset.Status == AssetStatus.Assigned &&
                operation.Kind is
                    AssetBulkChangeOperationKind.SetLocation or
                    AssetBulkChangeOperationKind.SetCondition or
                    AssetBulkChangeOperationKind.SetStatus)
            {
                throw new ResourceConflictException(
                    "bulk_custody_bypass_forbidden",
                    $"Asset '{asset.AssetTag}' is assigned. Use the custody workflow.");
            }
        }

        return assets;
    }

    private async Task ValidateOperationReferencesAsync(
        AssetBulkChangeOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.Kind == AssetBulkChangeOperationKind.SetCategory)
        {
            var category = await _inventoryRepository.GetCategoryAsync(
                operation.CategoryId!.Value,
                cancellationToken);
            if (category is null || !category.IsActive)
            {
                throw Validation(
                    "invalid_category",
                    "operation.categoryId",
                    "The selected category does not exist or is inactive.");
            }
        }

        if (operation.Kind == AssetBulkChangeOperationKind.SetLocation &&
            !operation.ClearLocation)
        {
            var location = await _inventoryRepository.GetLocationAsync(
                operation.LocationId!.Value,
                cancellationToken);
            if (location is null || !location.IsActive)
            {
                throw Validation(
                    "invalid_location",
                    "operation.locationId",
                    "The selected location does not exist or is inactive.");
            }
        }
    }

    private void ApplyChange(
        Asset asset,
        AssetBulkChangeOperation operation,
        AssetBulkChangeJob job,
        CurrentActor actor,
        DateTimeOffset now)
    {
        var preview = BuildPreviewItem(asset, operation);
        if (operation.Kind == AssetBulkChangeOperationKind.AddNote)
        {
            AddAssetEvent(
                asset,
                AssetEventType.NoteAdded,
                $"Bulk note added to asset {asset.AssetTag}.",
                preview,
                job,
                actor,
                now);
            return;
        }

        IReadOnlyDictionary<string, FieldChange> changes;
        try
        {
            changes = asset.Update(
                ToDetails(asset, operation),
                asset.Version,
                now);
        }
        catch (DomainRuleException exception)
        {
            throw MapDomainException(exception);
        }

        if (changes.Count == 0)
        {
            return;
        }

        var eventType = operation.Kind switch
        {
            AssetBulkChangeOperationKind.SetLocation => AssetEventType.LocationChanged,
            AssetBulkChangeOperationKind.SetCategory => AssetEventType.AssetDetailsChanged,
            AssetBulkChangeOperationKind.SetCondition => AssetEventType.ConditionChanged,
            AssetBulkChangeOperationKind.SetStatus => AssetEventType.StatusChanged,
            _ => throw new InvalidOperationException("Unsupported bulk operation."),
        };
        AddAssetEvent(
            asset,
            eventType,
            $"Asset {asset.AssetTag} changed by bulk operation.",
            preview,
            job,
            actor,
            now);
    }

    private void AddAssetEvent(
        Asset asset,
        AssetEventType eventType,
        string summary,
        AssetBulkChangePreviewItem preview,
        AssetBulkChangeJob job,
        CurrentActor actor,
        DateTimeOffset now)
    {
        _operationsRepository.AddAssetEvent(
            AssetEvent.Create(
                asset.Id,
                eventType,
                now,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                summary,
                JsonSerializer.Serialize(
                    new
                    {
                        BulkChangeJobId = job.Id,
                        Operation = job.OperationKind,
                        Field = preview.Field,
                        preview.From,
                        preview.To,
                        job.Reason,
                    },
                    JsonOptions),
                actor.CorrelationId));
    }

    private static AssetDetails ToDetails(
        Asset asset,
        AssetBulkChangeOperation operation) =>
        new(
            asset.AssetTag,
            operation.Kind == AssetBulkChangeOperationKind.SetCategory
                ? operation.CategoryId!.Value
                : asset.CategoryId,
            asset.Manufacturer,
            asset.Model,
            asset.SerialNumber,
            operation.Kind == AssetBulkChangeOperationKind.SetStatus
                ? operation.Status!.Value
                : asset.Status,
            operation.Kind == AssetBulkChangeOperationKind.SetCondition
                ? operation.Condition!.Value
                : asset.Condition,
            operation.Kind == AssetBulkChangeOperationKind.SetLocation
                ? operation.ClearLocation
                    ? null
                    : operation.LocationId
                : asset.LocationId,
            asset.PurchaseDate,
            asset.PurchaseCost,
            asset.Currency,
            asset.Supplier,
            asset.Notes,
            asset.LegacyId,
            asset.DeviceName,
            asset.Description,
            asset.LanMacAddress,
            asset.BiosGuid,
            asset.Ownership,
            asset.OperatingSystem,
            asset.OperatingSystemVersion,
            asset.Windows11Compliant);

    private static AssetBulkChangePreviewItem BuildPreviewItem(
        Asset asset,
        AssetBulkChangeOperation operation)
    {
        var (field, from, to) = operation.Kind switch
        {
            AssetBulkChangeOperationKind.SetLocation => (
                nameof(Asset.LocationId),
                asset.LocationId?.ToString(),
                operation.ClearLocation ? null : operation.LocationId?.ToString()),
            AssetBulkChangeOperationKind.SetCategory => (
                nameof(Asset.CategoryId),
                asset.CategoryId.ToString(),
                operation.CategoryId?.ToString()),
            AssetBulkChangeOperationKind.SetCondition => (
                nameof(Asset.Condition),
                asset.Condition.ToString(),
                operation.Condition?.ToString()),
            AssetBulkChangeOperationKind.SetStatus => (
                nameof(Asset.Status),
                asset.Status.ToString(),
                operation.Status?.ToString()),
            AssetBulkChangeOperationKind.AddNote => (
                "Note",
                null,
                operation.Note),
            _ => throw new InvalidOperationException("Unsupported bulk operation."),
        };
        return new AssetBulkChangePreviewItem(
            asset.Id,
            asset.AssetTag,
            asset.Version,
            field,
            from,
            to,
            operation.Kind != AssetBulkChangeOperationKind.AddNote &&
            string.Equals(from, to, StringComparison.Ordinal));
    }

    private static AssetBulkChangeSelection[] NormalizeSelections(
        IReadOnlyCollection<AssetBulkChangeSelection>? selections)
    {
        if (selections is null || selections.Count is < 1 or > 200)
        {
            throw Validation(
                "bulk_asset_count_invalid",
                "assets",
                "Bulk changes must contain between 1 and 200 assets.");
        }

        if (selections.Any(
                selection =>
                    selection.AssetId == Guid.Empty ||
                    selection.Version == Guid.Empty))
        {
            throw Validation(
                "bulk_asset_reference_invalid",
                "assets",
                "Every selected asset must include a non-empty assetId and version.");
        }

        if (selections.Select(selection => selection.AssetId).Distinct().Count() !=
            selections.Count)
        {
            throw Validation(
                "bulk_asset_duplicate",
                "assets",
                "An asset can appear only once in a bulk change.");
        }

        return selections
            .OrderBy(selection => selection.AssetId)
            .ToArray();
    }

    private static AssetBulkChangeOperation NormalizeAndValidateOperation(
        AssetBulkChangeOperation? operation)
    {
        if (operation is null)
        {
            throw Validation(
                "bulk_operation_required",
                "operation",
                "A bulk operation is required.");
        }

        var normalized = operation with
        {
            Note = string.IsNullOrWhiteSpace(operation.Note)
                ? null
                : operation.Note.Trim(),
        };
        var valid = normalized.Kind switch
        {
            AssetBulkChangeOperationKind.SetLocation =>
                normalized.ClearLocation != normalized.LocationId.HasValue &&
                NoOtherValues(normalized, allowLocation: true),
            AssetBulkChangeOperationKind.SetCategory =>
                normalized.CategoryId.HasValue &&
                normalized.CategoryId.Value != Guid.Empty &&
                NoOtherValues(normalized, allowCategory: true),
            AssetBulkChangeOperationKind.SetCondition =>
                normalized.Condition.HasValue &&
                NoOtherValues(normalized, allowCondition: true),
            AssetBulkChangeOperationKind.SetStatus =>
                normalized.Status is
                    AssetStatus.InStock or
                    AssetStatus.Reserved or
                    AssetStatus.Repair or
                    AssetStatus.Lost or
                    AssetStatus.Stolen &&
                NoOtherValues(normalized, allowStatus: true),
            AssetBulkChangeOperationKind.AddNote =>
                normalized.Note is { Length: <= 4000 } &&
                NoOtherValues(normalized, allowNote: true),
            _ => false,
        };
        if (!valid)
        {
            throw Validation(
                "bulk_operation_invalid",
                "operation",
                "Supply exactly the value required by the selected operation. " +
                "Assigned, retired and disposed statuses are not permitted.");
        }

        return normalized;
    }

    private static bool NoOtherValues(
        AssetBulkChangeOperation operation,
        bool allowLocation = false,
        bool allowCategory = false,
        bool allowCondition = false,
        bool allowStatus = false,
        bool allowNote = false) =>
        (allowLocation ||
         (!operation.LocationId.HasValue && !operation.ClearLocation)) &&
        (allowCategory || !operation.CategoryId.HasValue) &&
        (allowCondition || !operation.Condition.HasValue) &&
        (allowStatus || !operation.Status.HasValue) &&
        (allowNote || operation.Note is null);

    private static string NormalizeReason(string? reason)
    {
        var normalized = reason?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 1000)
        {
            throw Validation(
                "bulk_reason_invalid",
                "reason",
                "Reason is required and must be 1000 characters or fewer.");
        }

        return normalized;
    }

    private static string CreateFingerprint(
        AssetBulkChangeOperation operation,
        AssetBulkChangeSelection[] selections,
        string reason)
    {
        var canonical = JsonSerializer.Serialize(
            new
            {
                Operation = operation,
                Assets = selections,
                Reason = reason,
            },
            JsonOptions);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static AssetBulkChangeOperation DeserializeOperation(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AssetBulkChangeOperation>(
                       json,
                       JsonOptions)
                   ?? throw new JsonException("Operation is missing.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The persisted bulk operation is invalid.",
                exception);
        }
    }

    private static AssetBulkChangeSelection[] DeserializeSelections(
        string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AssetBulkChangeSelection[]>(
                       json,
                       JsonOptions)
                   ?? throw new JsonException("Selections are missing.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The persisted bulk selections are invalid.",
                exception);
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _operationsRepository.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new ResourceConflictException(
                exception.Code,
                exception.Message,
                exception);
        }
    }

    private static AssetBulkChangePreviewView ToPreviewView(
        AssetBulkChangeJob job,
        IReadOnlyList<AssetBulkChangePreviewItem> items) =>
        new(
            job.Id,
            job.Status,
            job.OperationKind,
            job.Reason,
            job.AssetCount,
            items,
            job.Fingerprint,
            job.CreatedAtUtc,
            job.ExpiresAtUtc,
            job.CommittedAtUtc,
            job.Version);

    private static RequestValidationException Validation(
        string code,
        string field,
        string message) =>
        new(
            code,
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    private static InventoryException MapDomainException(
        DomainRuleException exception) =>
        exception.Code is
            "concurrency_conflict" or
            "bulk_preview_fingerprint_mismatch" or
            "bulk_preview_already_committed" or
            "bulk_preview_expired"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);
}
