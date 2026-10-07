using Inventory.Application.Abstractions;
using Inventory.Application.Assets;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Auditing;
using Inventory.Domain.Locations;
using Inventory.Domain.Warranties;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class EfInventoryRepository : IInventoryRepository
{
    private readonly InventoryDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ISchoolSettingsProvider _schoolSettingsProvider;

    public EfInventoryRepository(
        InventoryDbContext dbContext,
        IClock clock,
        ISchoolSettingsProvider schoolSettingsProvider)
    {
        _dbContext = dbContext;
        _clock = clock;
        _schoolSettingsProvider = schoolSettingsProvider;
    }

    public Task<Asset?> GetAssetAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Assets.SingleOrDefaultAsync(asset => asset.Id == id, cancellationToken);

    public async Task<AssetView?> GetAssetViewAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        return await BuildAssetViewQuery(
                SchoolBusinessDate.FromUtc(_clock.UtcNow, settings.TimeZone),
                settings.WarrantyWarningDays,
                assetId: id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<AssetView?> GetAssetViewByTagAsync(
        string normalizedAssetTag,
        CancellationToken cancellationToken)
    {
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        return await BuildAssetViewQuery(
                SchoolBusinessDate.FromUtc(_clock.UtcNow, settings.TimeZone),
                settings.WarrantyWarningDays,
                normalizedAssetTag: normalizedAssetTag)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<PageResult<AssetListItem>> SearchAssetsAsync(
        AssetSearch search,
        CancellationToken cancellationToken)
    {
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        var today = SchoolBusinessDate.FromUtc(
            _clock.UtcNow,
            settings.TimeZone);
        var warningDate = today.AddDays(settings.WarrantyWarningDays);
        var query =
            from asset in _dbContext.Assets.AsNoTracking()
            join category in _dbContext.AssetCategories.AsNoTracking()
                on asset.CategoryId equals category.Id
            join location in _dbContext.Locations.AsNoTracking()
                on asset.LocationId equals location.Id into locationJoin
            from location in locationJoin.DefaultIfEmpty()
            join assignment in _dbContext.AssetAssignments
                    .AsNoTracking()
                    .Where(assignment => assignment.ReturnedAtUtc == null)
                on asset.Id equals assignment.AssetId into assignmentJoin
            from assignment in assignmentJoin.DefaultIfEmpty()
            join person in _dbContext.People.AsNoTracking()
                on assignment.PersonId equals (Guid?)person.Id into personJoin
            from person in personJoin.DefaultIfEmpty()
            join department in _dbContext.Departments.AsNoTracking()
                on assignment.DepartmentId equals (Guid?)department.Id into departmentJoin
            from department in departmentJoin.DefaultIfEmpty()
            join warranty in _dbContext.WarrantyCoverages
                    .AsNoTracking()
                    .Where(warranty =>
                        warranty.IsPrimary &&
                        warranty.ArchivedAtUtc == null)
                on asset.Id equals warranty.AssetId into warrantyJoin
            from warranty in warrantyJoin.DefaultIfEmpty()
            select new
            {
                Asset = asset,
                Category = category,
                Location = location,
                Assignment = assignment,
                Person = person,
                Department = department,
                Warranty = warranty,
            };

        if (!string.IsNullOrWhiteSpace(search.Query))
        {
            var term = search.Query.Trim();
            query = query.Where(row =>
                row.Asset.AssetTag.Contains(term) ||
                (row.Asset.DeviceName != null && row.Asset.DeviceName.Contains(term)) ||
                (row.Asset.Description != null && row.Asset.Description.Contains(term)) ||
                (row.Asset.SerialNumber != null && row.Asset.SerialNumber.Contains(term)) ||
                (row.Asset.LanMacAddress != null && row.Asset.LanMacAddress.Contains(term)) ||
                (row.Asset.Ownership != null && row.Asset.Ownership.Contains(term)) ||
                (row.Asset.Manufacturer != null && row.Asset.Manufacturer.Contains(term)) ||
                (row.Asset.Model != null && row.Asset.Model.Contains(term)) ||
                (row.Asset.OperatingSystem != null &&
                 row.Asset.OperatingSystem.Contains(term)) ||
                (row.Asset.OperatingSystemVersion != null &&
                 row.Asset.OperatingSystemVersion.Contains(term)) ||
                row.Category.Code.Contains(term) ||
                row.Category.Name.Contains(term) ||
                (row.Location != null && row.Location.Code.Contains(term)) ||
                (row.Location != null && row.Location.Name.Contains(term)) ||
                (row.Person != null &&
                 (row.Person.DisplayName.Contains(term) ||
                  (row.Person.ExternalId != null &&
                   row.Person.ExternalId.Contains(term)))) ||
                (row.Department != null &&
                 (row.Department.Name.Contains(term) ||
                  row.Department.Code.Contains(term))) ||
                (row.Assignment != null &&
                 row.Assignment.TargetDisplayName.Contains(term)) ||
                (row.Assignment != null &&
                 row.Assignment.TargetReference != null &&
                 row.Assignment.TargetReference.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(search.AssetTag))
        {
            query = query.Where(row => row.Asset.AssetTag.Contains(search.AssetTag));
        }

        if (!string.IsNullOrWhiteSpace(search.SerialNumber))
        {
            query = query.Where(row =>
                row.Asset.SerialNumber != null &&
                row.Asset.SerialNumber.Contains(search.SerialNumber));
        }

        if (search.CategoryId.HasValue)
        {
            query = query.Where(row => row.Asset.CategoryId == search.CategoryId.Value);
        }

        if (search.Status.HasValue)
        {
            query = query.Where(row => row.Asset.Status == search.Status.Value);
        }

        if (search.Condition.HasValue)
        {
            query = query.Where(row => row.Asset.Condition == search.Condition.Value);
        }

        if (search.LocationId.HasValue)
        {
            query = query.Where(row => row.Asset.LocationId == search.LocationId.Value);
        }

        if (search.DepartmentId.HasValue)
        {
            query = query.Where(
                row => row.Assignment != null &&
                       row.Assignment.DepartmentId == search.DepartmentId.Value);
        }

        if (search.AssignedPersonId.HasValue)
        {
            query = query.Where(
                row => row.Assignment != null &&
                       row.Assignment.PersonId == search.AssignedPersonId.Value);
        }

        if (search.Assigned.HasValue)
        {
            query = search.Assigned.Value
                ? query.Where(row => row.Assignment != null)
                : query.Where(row => row.Assignment == null);
        }

        if (search.WarrantyState.HasValue)
        {
            query = search.WarrantyState.Value switch
            {
                WarrantyState.NotRecorded => query.Where(row =>
                    row.Warranty == null ||
                    (!row.Warranty.StartDate.HasValue &&
                     !row.Warranty.EndDate.HasValue)),
                WarrantyState.Future => query.Where(row =>
                    row.Warranty != null &&
                    row.Warranty.StartDate.HasValue &&
                    row.Warranty.StartDate.Value > today),
                WarrantyState.Expired => query.Where(row =>
                    row.Warranty != null &&
                    row.Warranty.EndDate.HasValue &&
                    row.Warranty.EndDate.Value < today),
                WarrantyState.ExpiringSoon => query.Where(row =>
                    row.Warranty != null &&
                    (!row.Warranty.StartDate.HasValue ||
                     row.Warranty.StartDate.Value <= today) &&
                    row.Warranty.EndDate.HasValue &&
                    row.Warranty.EndDate.Value >= today &&
                    row.Warranty.EndDate.Value <= warningDate),
                WarrantyState.Active => query.Where(row =>
                    row.Warranty != null &&
                    (!row.Warranty.StartDate.HasValue ||
                     row.Warranty.StartDate.Value <= today) &&
                    (row.Warranty.StartDate.HasValue ||
                     row.Warranty.EndDate.HasValue) &&
                    (!row.Warranty.EndDate.HasValue ||
                     row.Warranty.EndDate.Value > warningDate)),
                _ => query,
            };
        }

        if (search.WarrantyEndsBefore.HasValue)
        {
            query = query.Where(row =>
                row.Warranty != null &&
                row.Warranty.EndDate.HasValue &&
                row.Warranty.EndDate.Value <= search.WarrantyEndsBefore.Value);
        }

        if (search.CreatedFrom.HasValue)
        {
            query = query.Where(row =>
                row.Asset.CreatedAtUtc >= search.CreatedFrom.Value);
        }

        if (search.CreatedTo.HasValue)
        {
            query = query.Where(row =>
                row.Asset.CreatedAtUtc <= search.CreatedTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(search.LegacyId))
        {
            query = query.Where(row => row.Asset.LegacyId == search.LegacyId);
        }

        query = search.Sort switch
        {
            "-assetTag" => query.OrderByDescending(row => row.Asset.AssetTag),
            "updatedAtUtc" => query.OrderBy(row => row.Asset.UpdatedAtUtc),
            "-updatedAtUtc" => query.OrderByDescending(row => row.Asset.UpdatedAtUtc),
            "status" => query
                .OrderBy(row => row.Asset.Status)
                .ThenBy(row => row.Asset.AssetTag),
            "-status" => query
                .OrderByDescending(row => row.Asset.Status)
                .ThenBy(row => row.Asset.AssetTag),
            _ => query.OrderBy(row => row.Asset.AssetTag),
        };

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(row => new AssetListItem(
                row.Asset.Id,
                row.Asset.AssetTag,
                row.Asset.DeviceName,
                row.Category.Id,
                row.Category.Code,
                row.Category.Name,
                row.Asset.Description,
                row.Asset.Manufacturer,
                row.Asset.Model,
                row.Asset.SerialNumber,
                row.Asset.LanMacAddress,
                row.Asset.BiosGuid,
                row.Asset.Ownership,
                row.Asset.Status,
                row.Asset.Condition,
                row.Location == null ? null : row.Location.Id,
                row.Location == null ? null : row.Location.Code,
                row.Location == null ? null : row.Location.Name,
                row.Assignment == null
                    ? null
                    : new CurrentAssignmentView(
                        row.Assignment.Id,
                        row.Assignment.PersonId.HasValue
                            ? AssignmentTargetType.Person
                            : AssignmentTargetType.Department,
                        row.Assignment.PersonId,
                        row.Assignment.DepartmentId,
                        row.Assignment.PersonId.HasValue && row.Person != null
                            ? row.Person.DisplayName
                            : row.Department != null
                                ? row.Department.Name
                                : row.Assignment.TargetDisplayName,
                        row.Assignment.PersonId.HasValue && row.Person != null
                            ? row.Person.ExternalId
                            : row.Department != null
                                ? row.Department.Code
                                : row.Assignment.TargetReference,
                        row.Assignment.AssignedAtUtc,
                        row.Assignment.Version,
                        row.Assignment.ExpectedReturnOn,
                        row.Assignment.ExpectedReturnOn != null &&
                        row.Assignment.ExpectedReturnOn < today),
                new WarrantySummaryView(
                    row.Warranty == null ||
                    (!row.Warranty.StartDate.HasValue &&
                     !row.Warranty.EndDate.HasValue)
                        ? WarrantyState.NotRecorded
                        : row.Warranty.StartDate.HasValue &&
                          row.Warranty.StartDate.Value > today
                            ? WarrantyState.Future
                            : row.Warranty.EndDate.HasValue &&
                              row.Warranty.EndDate.Value < today
                                ? WarrantyState.Expired
                                : row.Warranty.EndDate.HasValue &&
                                  row.Warranty.EndDate.Value <= warningDate
                                    ? WarrantyState.ExpiringSoon
                                    : WarrantyState.Active,
                    row.Warranty == null ? null : row.Warranty.Provider,
                    row.Warranty == null ? null : row.Warranty.EndDate,
                    row.Warranty == null || !row.Warranty.EndDate.HasValue
                        ? null
                        : row.Warranty.EndDate.Value.DayNumber - today.DayNumber),
                row.Asset.OperatingSystem,
                row.Asset.OperatingSystemVersion,
                row.Asset.Windows11Compliant,
                row.Asset.UpdatedAtUtc,
                row.Asset.Version))
            .ToArrayAsync(cancellationToken);

        return new PageResult<AssetListItem>(
            items,
            search.Page,
            search.PageSize,
            totalCount);
    }

    public Task<bool> AssetTagExistsAsync(
        string normalizedAssetTag,
        Guid? excludingAssetId,
        CancellationToken cancellationToken) =>
        _dbContext.Assets.AnyAsync(
            asset =>
                asset.AssetTagNormalized == normalizedAssetTag &&
                (!excludingAssetId.HasValue || asset.Id != excludingAssetId.Value),
            cancellationToken);

    public Task<bool> SerialNumberExistsAsync(
        string normalizedSerialNumber,
        Guid? excludingAssetId,
        CancellationToken cancellationToken) =>
        _dbContext.Assets.AnyAsync(
            asset =>
                asset.SerialNumberNormalized == normalizedSerialNumber &&
                (!excludingAssetId.HasValue || asset.Id != excludingAssetId.Value),
            cancellationToken);

    public Task<Guid?> GetLinkedManagedAgentIdAsync(
        Guid assetId,
        CancellationToken cancellationToken) =>
        _dbContext.ManagedAgents
            .AsNoTracking()
            .Where(agent => agent.LinkedAssetId == assetId)
            .Select(agent => (Guid?)agent.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<AssetCategory?> GetCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.AssetCategories.SingleOrDefaultAsync(
            category => category.Id == id,
            cancellationToken);

    public Task<bool> CategoryCodeExistsAsync(
        string normalizedCode,
        CancellationToken cancellationToken) =>
        _dbContext.AssetCategories.AnyAsync(
            category => category.CodeNormalized == normalizedCode,
            cancellationToken);

    public async Task<IReadOnlyList<AssetCategory>> ListCategoriesAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.AssetCategories.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(category => category.IsActive);
        }

        return await query.OrderBy(category => category.Name).ToArrayAsync(cancellationToken);
    }

    public Task<Location?> GetLocationAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Locations.SingleOrDefaultAsync(
            location => location.Id == id,
            cancellationToken);

    public Task<bool> LocationCodeExistsAsync(
        string normalizedCode,
        CancellationToken cancellationToken) =>
        _dbContext.Locations.AnyAsync(
            location => location.CodeNormalized == normalizedCode,
            cancellationToken);

    public async Task<IReadOnlyList<Location>> ListLocationsAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Locations.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(location => location.IsActive);
        }

        return await query
            .OrderBy(location => location.Code)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<PageResult<AssetEvent>> GetAssetHistoryAsync(
        Guid assetId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.AssetEvents
            .AsNoTracking()
            .Where(assetEvent => assetEvent.AssetId == assetId);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(assetEvent => assetEvent.OccurredAtUtc)
            .ThenByDescending(assetEvent => assetEvent.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return new PageResult<AssetEvent>(items, page, pageSize, totalCount);
    }

    public void AddAsset(Asset asset) => _dbContext.Assets.Add(asset);

    public void AddAssetEvent(AssetEvent assetEvent) => _dbContext.AssetEvents.Add(assetEvent);

    public void AddCategory(AssetCategory category) => _dbContext.AssetCategories.Add(category);

    public void AddLocation(Location location) => _dbContext.Locations.Add(location);

    public void AddAuditLog(AuditLog auditLog) => _dbContext.AuditLogs.Add(auditLog);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesWithConflictMappingAsync(
                "data_conflict",
                "The requested change conflicts with existing data.",
                "The record was changed by another user.",
                cancellationToken);
        }
        catch (PersistenceConflictException exception)
            when (exception.GetBaseException().Message.Contains(
                "asset_has_linked_agent",
                StringComparison.Ordinal))
        {
            throw new PersistenceConflictException(
                "asset_has_linked_agent",
                "Unlink the managed endpoint agent before retiring or disposing this asset.",
                exception);
        }
        catch (PersistenceConflictException exception)
            when (exception.GetBaseException().Message.Contains(
                "asset_has_active_bundle",
                StringComparison.Ordinal))
        {
            throw new PersistenceConflictException(
                "asset_has_active_bundle",
                "Remove the asset from its active bundle before retiring or disposing it.",
                exception);
        }
        catch (PersistenceConflictException exception)
            when (exception.GetBaseException().Message.Contains(
                "attachment_quota_exceeded",
                StringComparison.Ordinal))
        {
            throw new PersistenceConflictException(
                "attachment_quota_exceeded",
                "The attachment would exceed this asset's storage quota.",
                exception);
        }
    }

    private IQueryable<AssetView> BuildAssetViewQuery(
        DateOnly today,
        int warrantyWarningDays,
        Guid? assetId = null,
        string? normalizedAssetTag = null)
    {
        var warningDate = today.AddDays(warrantyWarningDays);
        var query =
            from asset in _dbContext.Assets.AsNoTracking()
            join category in _dbContext.AssetCategories.AsNoTracking()
                on asset.CategoryId equals category.Id
            join location in _dbContext.Locations.AsNoTracking()
                on asset.LocationId equals location.Id into locationJoin
            from location in locationJoin.DefaultIfEmpty()
            join assignment in _dbContext.AssetAssignments
                    .AsNoTracking()
                    .Where(assignment => assignment.ReturnedAtUtc == null)
                on asset.Id equals assignment.AssetId into assignmentJoin
            from assignment in assignmentJoin.DefaultIfEmpty()
            join person in _dbContext.People.AsNoTracking()
                on assignment.PersonId equals (Guid?)person.Id into personJoin
            from person in personJoin.DefaultIfEmpty()
            join department in _dbContext.Departments.AsNoTracking()
                on assignment.DepartmentId equals (Guid?)department.Id into departmentJoin
            from department in departmentJoin.DefaultIfEmpty()
            join warranty in _dbContext.WarrantyCoverages
                    .AsNoTracking()
                    .Where(warranty =>
                        warranty.IsPrimary &&
                        warranty.ArchivedAtUtc == null)
                on asset.Id equals warranty.AssetId into warrantyJoin
            from warranty in warrantyJoin.DefaultIfEmpty()
            select new
            {
                Asset = asset,
                Category = category,
                Location = location,
                Assignment = assignment,
                Person = person,
                Department = department,
                Warranty = warranty,
            };

        if (assetId.HasValue)
        {
            query = query.Where(row => row.Asset.Id == assetId.Value);
        }

        if (normalizedAssetTag is not null)
        {
            query = query.Where(
                row => row.Asset.AssetTagNormalized == normalizedAssetTag);
        }

        return query.Select(row => new AssetView(
            row.Asset.Id,
            row.Asset.AssetTag,
            row.Asset.DeviceName,
            row.Category.Id,
            row.Category.Code,
            row.Category.Name,
            row.Asset.Description,
            row.Asset.Manufacturer,
            row.Asset.Model,
            row.Asset.SerialNumber,
            row.Asset.LanMacAddress,
            row.Asset.BiosGuid,
            row.Asset.Ownership,
            row.Asset.Status,
            row.Asset.Condition,
            row.Location == null ? null : row.Location.Id,
            row.Location == null ? null : row.Location.Code,
            row.Location == null ? null : row.Location.Name,
            row.Assignment == null
                ? null
                : new CurrentAssignmentView(
                    row.Assignment.Id,
                    row.Assignment.PersonId.HasValue
                        ? AssignmentTargetType.Person
                        : AssignmentTargetType.Department,
                    row.Assignment.PersonId,
                    row.Assignment.DepartmentId,
                    row.Assignment.PersonId.HasValue && row.Person != null
                        ? row.Person.DisplayName
                        : row.Department != null
                            ? row.Department.Name
                            : row.Assignment.TargetDisplayName,
                    row.Assignment.PersonId.HasValue && row.Person != null
                        ? row.Person.ExternalId
                        : row.Department != null
                            ? row.Department.Code
                            : row.Assignment.TargetReference,
                    row.Assignment.AssignedAtUtc,
                    row.Assignment.Version,
                    row.Assignment.ExpectedReturnOn,
                    row.Assignment.ExpectedReturnOn != null &&
                    row.Assignment.ExpectedReturnOn < today),
            new WarrantySummaryView(
                row.Warranty == null ||
                (!row.Warranty.StartDate.HasValue &&
                 !row.Warranty.EndDate.HasValue)
                    ? WarrantyState.NotRecorded
                    : row.Warranty.StartDate.HasValue &&
                      row.Warranty.StartDate.Value > today
                        ? WarrantyState.Future
                        : row.Warranty.EndDate.HasValue &&
                          row.Warranty.EndDate.Value < today
                            ? WarrantyState.Expired
                            : row.Warranty.EndDate.HasValue &&
                              row.Warranty.EndDate.Value <= warningDate
                                ? WarrantyState.ExpiringSoon
                                : WarrantyState.Active,
                row.Warranty == null ? null : row.Warranty.Provider,
                row.Warranty == null ? null : row.Warranty.EndDate,
                row.Warranty == null || !row.Warranty.EndDate.HasValue
                    ? null
                    : row.Warranty.EndDate.Value.DayNumber - today.DayNumber),
            row.Asset.OperatingSystem,
            row.Asset.OperatingSystemVersion,
            row.Asset.Windows11Compliant,
            row.Asset.PurchaseDate,
            row.Asset.PurchaseCost,
            row.Asset.Currency,
            row.Asset.Supplier,
            row.Asset.Notes,
            row.Asset.LegacyId,
            row.Asset.CreatedAtUtc,
            row.Asset.UpdatedAtUtc,
            row.Asset.RetiredAtUtc,
            row.Asset.RetirementReason,
            row.Asset.DisposedAtUtc,
            row.Asset.DisposalReason,
            row.Asset.Version,
            row.Asset.ReportedSerialNumber));
    }
}
