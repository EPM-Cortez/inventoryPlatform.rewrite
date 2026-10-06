using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Common;
using Inventory.Domain.Warranties;

namespace Inventory.Application.Warranties;

public sealed class WarrantyService
{
    private static readonly JsonSerializerOptions EventJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IInventoryRepository _inventoryRepository;
    private readonly IWarrantyRepository _warrantyRepository;
    private readonly ISchoolSettingsProvider _schoolSettingsProvider;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;

    public WarrantyService(
        IInventoryRepository inventoryRepository,
        IWarrantyRepository warrantyRepository,
        ISchoolSettingsProvider schoolSettingsProvider,
        IClock clock,
        ICurrentUserContext currentUser)
    {
        _inventoryRepository = inventoryRepository;
        _warrantyRepository = warrantyRepository;
        _schoolSettingsProvider = schoolSettingsProvider;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<WarrantyView>> ListForAssetAsync(
        Guid assetId,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        _ = await RequireAssetAsync(assetId, cancellationToken);
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        var warranties = await _warrantyRepository.ListForAssetAsync(
            assetId,
            includeArchived,
            cancellationToken);
        var today = SchoolBusinessDate.FromUtc(_clock.UtcNow, settings.TimeZone);
        return warranties
            .Select(
                warranty => ToView(
                    warranty,
                    settings.WarrantyWarningDays,
                    today))
            .ToArray();
    }

    public async Task<WarrantyView> AddAsync(
        Guid assetId,
        CreateWarrantyCommand command,
        CancellationToken cancellationToken)
    {
        var asset = await RequireAssetAsync(assetId, cancellationToken);
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        var replacedPrimary = command.IsPrimary
            ? await _warrantyRepository.GetPrimaryAsync(
                assetId,
                excludingWarrantyId: null,
                cancellationToken)
            : null;
        WarrantyCoverage warranty;

        try
        {
            if (replacedPrimary is not null)
            {
                Demote(replacedPrimary, now);
            }

            warranty = WarrantyCoverage.Create(
                assetId,
                command.Provider,
                command.CoverageType,
                command.StartDate,
                command.EndDate,
                command.Reference,
                command.Notes,
                command.IsPrimary,
                now);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _warrantyRepository.Add(warranty);
        _inventoryRepository.AddAssetEvent(
            AssetEvent.Create(
                asset.Id,
                AssetEventType.WarrantyAdded,
                now,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                $"Warranty coverage from {warranty.Provider} was added to asset {asset.AssetTag}.",
                JsonSerializer.Serialize(
                    new
                    {
                        warrantyId = warranty.Id,
                        warranty.Provider,
                        warranty.CoverageType,
                        warranty.StartDate,
                        warranty.EndDate,
                        warranty.Reference,
                        warranty.IsPrimary,
                        replacedPrimaryWarrantyId = replacedPrimary?.Id,
                    },
                    EventJsonOptions),
                actor.CorrelationId));

        await SaveChangesAsync(cancellationToken);
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        return ToView(
            warranty,
            settings.WarrantyWarningDays,
            SchoolBusinessDate.FromUtc(_clock.UtcNow, settings.TimeZone));
    }

    public async Task<WarrantyView> UpdateAsync(
        Guid warrantyId,
        UpdateWarrantyCommand command,
        CancellationToken cancellationToken)
    {
        var warranty = await _warrantyRepository.GetAsync(warrantyId, cancellationToken)
            ?? throw new ResourceNotFoundException("Warranty", warrantyId);
        var asset = await RequireAssetAsync(warranty.AssetId, cancellationToken);
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;
        var before = Snapshot(warranty);
        var replacedPrimary = command.IsPrimary && !warranty.IsPrimary
            ? await _warrantyRepository.GetPrimaryAsync(
                warranty.AssetId,
                warranty.Id,
                cancellationToken)
            : null;

        try
        {
            if (replacedPrimary is not null)
            {
                Demote(replacedPrimary, now);
            }

            warranty.Update(
                command.Provider,
                command.CoverageType,
                command.StartDate,
                command.EndDate,
                command.Reference,
                command.Notes,
                command.IsPrimary,
                command.Version,
                now);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _inventoryRepository.AddAssetEvent(
            AssetEvent.Create(
                asset.Id,
                AssetEventType.WarrantyChanged,
                now,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                $"Warranty coverage for asset {asset.AssetTag} was changed.",
                JsonSerializer.Serialize(
                    new
                    {
                        warrantyId = warranty.Id,
                        before,
                        after = Snapshot(warranty),
                        replacedPrimaryWarrantyId = replacedPrimary?.Id,
                    },
                    EventJsonOptions),
                actor.CorrelationId));

        await SaveChangesAsync(cancellationToken);
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        return ToView(
            warranty,
            settings.WarrantyWarningDays,
            SchoolBusinessDate.FromUtc(_clock.UtcNow, settings.TimeZone));
    }

    public async Task<WarrantyView> ArchiveAsync(
        Guid warrantyId,
        Guid version,
        CancellationToken cancellationToken)
    {
        var warranty = await _warrantyRepository.GetAsync(warrantyId, cancellationToken)
            ?? throw new ResourceNotFoundException("Warranty", warrantyId);
        var asset = await RequireAssetAsync(warranty.AssetId, cancellationToken);
        var actor = _currentUser.RequireActor();
        var now = _clock.UtcNow;

        try
        {
            warranty.Archive(version, now);
        }
        catch (DomainRuleException exception)
        {
            throw ToApplicationException(exception);
        }

        _inventoryRepository.AddAssetEvent(
            AssetEvent.Create(
                asset.Id,
                AssetEventType.WarrantyArchived,
                now,
                actor.UserId,
                actor.DisplayName,
                AssetEventSource.Api,
                $"Warranty coverage for asset {asset.AssetTag} was archived.",
                JsonSerializer.Serialize(
                    new
                    {
                        warrantyId = warranty.Id,
                        warranty.Provider,
                        warranty.CoverageType,
                    },
                    EventJsonOptions),
                actor.CorrelationId));

        await SaveChangesAsync(cancellationToken);
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        return ToView(
            warranty,
            settings.WarrantyWarningDays,
            SchoolBusinessDate.FromUtc(_clock.UtcNow, settings.TimeZone));
    }

    public async Task<PageResult<WarrantyView>> ListExpiringAsync(
        int? withinDays,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        var effectiveDays = withinDays ?? settings.WarrantyWarningDays;
        if (effectiveDays is < 1 or > 365)
        {
            throw new RequestValidationException(
                "invalid_warranty_window",
                "withinDays must be between 1 and 365.",
                new Dictionary<string, string[]>
                {
                    ["withinDays"] = ["withinDays must be between 1 and 365."],
                });
        }

        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        var today = SchoolBusinessDate.FromUtc(_clock.UtcNow, settings.TimeZone);
        var result = await _warrantyRepository.ListExpiringAsync(
            today,
            today.AddDays(effectiveDays),
            normalizedPage.Page,
            normalizedPage.PageSize,
            cancellationToken);

        return new PageResult<WarrantyView>(
            result.Items
                .Select(
                    warranty => ToView(
                        warranty,
                        settings.WarrantyWarningDays,
                        today))
                .ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    private async Task<Asset> RequireAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken) =>
        await _inventoryRepository.GetAssetAsync(assetId, cancellationToken)
        ?? throw new ResourceNotFoundException("Asset", assetId);

    private static void Demote(WarrantyCoverage warranty, DateTimeOffset now) =>
        warranty.Update(
            warranty.Provider,
            warranty.CoverageType,
            warranty.StartDate,
            warranty.EndDate,
            warranty.Reference,
            warranty.Notes,
            isPrimary: false,
            warranty.Version,
            now);

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _warrantyRepository.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException exception)
        {
            throw new ResourceConflictException(exception.Code, exception.Message, exception);
        }
    }

    private static WarrantyView ToView(
        WarrantyCoverage warranty,
        int warningDays,
        DateOnly today) =>
        new(
            warranty.Id,
            warranty.AssetId,
            warranty.Provider,
            warranty.CoverageType,
            warranty.StartDate,
            warranty.EndDate,
            warranty.Reference,
            warranty.Notes,
            warranty.IsPrimary,
            warranty.CalculateState(today, warningDays),
            warranty.EndDate.HasValue
                ? warranty.EndDate.Value.DayNumber - today.DayNumber
                : null,
            warranty.CreatedAtUtc,
            warranty.UpdatedAtUtc,
            warranty.ArchivedAtUtc,
            warranty.Version);

    private static object Snapshot(WarrantyCoverage warranty) =>
        new
        {
            warranty.Provider,
            warranty.CoverageType,
            warranty.StartDate,
            warranty.EndDate,
            warranty.Reference,
            warranty.Notes,
            warranty.IsPrimary,
        };

    private static InventoryException ToApplicationException(DomainRuleException exception) =>
        exception.Code == "concurrency_conflict"
            ? new ResourceConflictException(exception.Code, exception.Message)
            : new RequestValidationException(exception.Code, exception.Message);
}
