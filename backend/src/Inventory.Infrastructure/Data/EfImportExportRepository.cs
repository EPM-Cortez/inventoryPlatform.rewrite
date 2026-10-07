using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Application.DataExchange;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Auditing;
using Inventory.Domain.Importing;
using Inventory.Domain.Warranties;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class EfImportExportRepository : IImportExportRepository
{
    private readonly InventoryDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ISchoolSettingsProvider _schoolSettingsProvider;

    public EfImportExportRepository(
        InventoryDbContext dbContext,
        IClock clock,
        ISchoolSettingsProvider schoolSettingsProvider)
    {
        _dbContext = dbContext;
        _clock = clock;
        _schoolSettingsProvider = schoolSettingsProvider;
    }

    public Task<AssetImportJob?> GetImportJobAsync(
        Guid importJobId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var query = forUpdate
            ? _dbContext.AssetImportJobs
            : _dbContext.AssetImportJobs.AsNoTracking();
        return query.SingleOrDefaultAsync(job => job.Id == importJobId, cancellationToken);
    }

    public async Task<ImportLookupSnapshot> GetImportLookupsAsync(
        ImportLookupRequest request,
        CancellationToken cancellationToken)
    {
        var categories = await _dbContext.AssetCategories
            .AsNoTracking()
            .Select(
                category => new ImportReferenceLookup(
                    category.Id,
                    category.Code,
                    category.Name,
                    category.IsActive,
                    category.Version))
            .ToArrayAsync(cancellationToken);
        var locations = await _dbContext.Locations
            .AsNoTracking()
            .Select(
                location => new ImportReferenceLookup(
                    location.Id,
                    location.Code,
                    location.Name,
                    location.IsActive,
                    location.Version))
            .ToArrayAsync(cancellationToken);
        var departments = await _dbContext.Departments
            .AsNoTracking()
            .Where(
                department =>
                    request.DepartmentCodes.Contains(department.CodeNormalized))
            .Select(
                department => new ImportReferenceLookup(
                    department.Id,
                    department.Code,
                    department.Name,
                    department.IsActive,
                    department.Version))
            .ToArrayAsync(cancellationToken);
        var people = await _dbContext.People
            .AsNoTracking()
            .Where(person => person.ExternalIdNormalized != null)
            .Select(
                person => new ImportPersonLookup(
                    person.Id,
                    person.ExternalId!,
                    person.DisplayName,
                    person.IsActive,
                    person.Version))
            .ToArrayAsync(cancellationToken);

        var assets = await QueryAssetsByIdentifiers(
                request.AssetTags,
                request.SerialNumbers,
                cancellationToken)
            .ConfigureAwait(false);
        var assetIds = assets.Select(asset => asset.Id).ToArray();
        var assignments = assetIds.Length == 0
            ? []
            : await _dbContext.AssetAssignments
                .AsNoTracking()
                .Where(
                    assignment =>
                        assetIds.Contains(assignment.AssetId) &&
                        assignment.ReturnedAtUtc == null)
                .Select(
                    assignment => new ImportAssignmentLookup(
                        assignment.Id,
                        assignment.AssetId,
                        assignment.PersonId,
                        assignment.DepartmentId,
                        assignment.Version))
                .ToArrayAsync(cancellationToken);
        var warranties = assetIds.Length == 0
            ? []
            : await _dbContext.WarrantyCoverages
                .AsNoTracking()
                .Where(
                    warranty =>
                        assetIds.Contains(warranty.AssetId) &&
                        warranty.IsPrimary &&
                        warranty.ArchivedAtUtc == null)
                .Select(
                    warranty => new ImportWarrantyLookup(
                        warranty.Id,
                        warranty.AssetId,
                        warranty.Provider,
                        warranty.CoverageType,
                        warranty.StartDate,
                        warranty.EndDate,
                        warranty.Reference,
                        warranty.Version))
                .ToArrayAsync(cancellationToken);
        var assignmentsByAsset = assignments.ToDictionary(assignment => assignment.AssetId);
        var warrantiesByAsset = warranties.ToDictionary(warranty => warranty.AssetId);

        return new ImportLookupSnapshot(
            categories,
            locations,
            departments,
            people,
            assets
                .Select(
                    asset => new ImportAssetLookup(
                        asset.Id,
                        asset.AssetTagNormalized,
                        asset.SerialNumber,
                        asset.ReportedSerialNumber,
                        asset.SerialNumberNormalized,
                        asset.Status,
                        asset.Version,
                        assignmentsByAsset.GetValueOrDefault(asset.Id),
                        warrantiesByAsset.GetValueOrDefault(asset.Id)))
                .ToArray());
    }

    public async Task<ImportCommitState> GetImportCommitStateAsync(
        ImportCommitLookupRequest request,
        CancellationToken cancellationToken)
    {
        var assets = await QueryAssetsForCommitAsync(request, cancellationToken);
        var categoryKeys = request.CategoryKeys.ToHashSet(StringComparer.Ordinal);
        var categories = request.CategoryIds.Count == 0 && categoryKeys.Count == 0
            ? []
            : (await _dbContext.AssetCategories
                    .ToArrayAsync(cancellationToken))
                .Where(
                    category =>
                        request.CategoryIds.Contains(category.Id) ||
                        categoryKeys.Contains(category.CodeNormalized) ||
                        categoryKeys.Contains(category.Name.Trim().ToUpperInvariant()))
                .ToArray();
        var locationKeys = request.LocationKeys.ToHashSet(StringComparer.Ordinal);
        var locations = request.LocationIds.Count == 0 && locationKeys.Count == 0
            ? []
            : (await _dbContext.Locations
                    .ToArrayAsync(cancellationToken))
                .Where(
                    location =>
                        request.LocationIds.Contains(location.Id) ||
                        locationKeys.Contains(location.CodeNormalized) ||
                        locationKeys.Contains(location.Name.Trim().ToUpperInvariant()))
                .ToArray();
        var people = request.PersonIds.Count == 0
            ? []
            : await _dbContext.People
                .Where(person => request.PersonIds.Contains(person.Id))
                .ToArrayAsync(cancellationToken);
        var departments = request.DepartmentIds.Count == 0
            ? []
            : await _dbContext.Departments
                .Where(department => request.DepartmentIds.Contains(department.Id))
                .ToArrayAsync(cancellationToken);
        var assignments = request.AssetIds.Count == 0
            ? []
            : await _dbContext.AssetAssignments
                .Where(
                    assignment =>
                        request.AssetIds.Contains(assignment.AssetId) &&
                        assignment.ReturnedAtUtc == null)
                .ToArrayAsync(cancellationToken);
        var warranties = request.AssetIds.Count == 0
            ? []
            : await _dbContext.WarrantyCoverages
                .Where(
                    warranty =>
                        request.AssetIds.Contains(warranty.AssetId) &&
                        warranty.IsPrimary &&
                        warranty.ArchivedAtUtc == null)
                .ToArrayAsync(cancellationToken);

        return new ImportCommitState(
            assets,
            categories,
            locations,
            people,
            departments,
            assignments,
            warranties);
    }

    public async Task<IReadOnlyList<AssetExportRow>> ExportAssetsAsync(
        AssetExportFilter filter,
        int take,
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
                    .Where(
                        warranty =>
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

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = filter.Query;
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
                  row.Department.Code.Contains(term))));
        }

        if (!string.IsNullOrWhiteSpace(filter.AssetTag))
        {
            query = query.Where(row => row.Asset.AssetTag.Contains(filter.AssetTag));
        }

        if (!string.IsNullOrWhiteSpace(filter.SerialNumber))
        {
            query = query.Where(
                row =>
                    row.Asset.SerialNumber != null &&
                    row.Asset.SerialNumber.Contains(filter.SerialNumber));
        }

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(row => row.Asset.CategoryId == filter.CategoryId.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(row => row.Asset.Status == filter.Status.Value);
        }

        if (filter.Condition.HasValue)
        {
            query = query.Where(row => row.Asset.Condition == filter.Condition.Value);
        }

        if (filter.LocationId.HasValue)
        {
            query = query.Where(row => row.Asset.LocationId == filter.LocationId.Value);
        }

        if (filter.DepartmentId.HasValue)
        {
            query = query.Where(
                row =>
                    row.Assignment != null &&
                    row.Assignment.DepartmentId == filter.DepartmentId.Value);
        }

        if (filter.AssignedPersonId.HasValue)
        {
            query = query.Where(
                row =>
                    row.Assignment != null &&
                    row.Assignment.PersonId == filter.AssignedPersonId.Value);
        }

        if (filter.Assigned.HasValue)
        {
            query = filter.Assigned.Value
                ? query.Where(row => row.Assignment != null)
                : query.Where(row => row.Assignment == null);
        }

        if (filter.WarrantyState.HasValue)
        {
            query = filter.WarrantyState.Value switch
            {
                WarrantyState.NotRecorded => query.Where(
                    row =>
                        row.Warranty == null ||
                        (!row.Warranty.StartDate.HasValue &&
                         !row.Warranty.EndDate.HasValue)),
                WarrantyState.Future => query.Where(
                    row =>
                        row.Warranty != null &&
                        row.Warranty.StartDate.HasValue &&
                        row.Warranty.StartDate.Value > today),
                WarrantyState.Expired => query.Where(
                    row =>
                        row.Warranty != null &&
                        row.Warranty.EndDate.HasValue &&
                        row.Warranty.EndDate.Value < today),
                WarrantyState.ExpiringSoon => query.Where(
                    row =>
                        row.Warranty != null &&
                        (!row.Warranty.StartDate.HasValue ||
                         row.Warranty.StartDate.Value <= today) &&
                        row.Warranty.EndDate.HasValue &&
                        row.Warranty.EndDate.Value >= today &&
                        row.Warranty.EndDate.Value <= warningDate),
                WarrantyState.Active => query.Where(
                    row =>
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

        if (filter.WarrantyEndsBefore.HasValue)
        {
            query = query.Where(
                row =>
                    row.Warranty != null &&
                    row.Warranty.EndDate.HasValue &&
                    row.Warranty.EndDate.Value <= filter.WarrantyEndsBefore.Value);
        }

        if (filter.CreatedFrom.HasValue)
        {
            query = query.Where(row => row.Asset.CreatedAtUtc >= filter.CreatedFrom.Value);
        }

        if (filter.CreatedTo.HasValue)
        {
            query = query.Where(row => row.Asset.CreatedAtUtc <= filter.CreatedTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.LegacyId))
        {
            query = query.Where(row => row.Asset.LegacyId == filter.LegacyId);
        }

        query = filter.Sort switch
        {
            "-assetTag" => query.OrderByDescending(row => row.Asset.AssetTag),
            "updatedAtUtc" => query
                .OrderBy(row => row.Asset.UpdatedAtUtc)
                .ThenBy(row => row.Asset.AssetTag),
            "-updatedAtUtc" => query
                .OrderByDescending(row => row.Asset.UpdatedAtUtc)
                .ThenBy(row => row.Asset.AssetTag),
            "status" => query
                .OrderBy(row => row.Asset.Status)
                .ThenBy(row => row.Asset.AssetTag),
            "-status" => query
                .OrderByDescending(row => row.Asset.Status)
                .ThenBy(row => row.Asset.AssetTag),
            _ => query.OrderBy(row => row.Asset.AssetTag),
        };

        return await query
            .Take(take)
            .Select(
                row => new AssetExportRow(
                    row.Asset.AssetTag,
                    row.Asset.DeviceName,
                    row.Category.Code,
                    row.Asset.Description,
                    row.Asset.Manufacturer,
                    row.Asset.Model,
                    row.Asset.SerialNumber,
                    row.Asset.LanMacAddress,
                    row.Asset.BiosGuid,
                    row.Asset.Ownership,
                    row.Asset.Status,
                    row.Asset.Condition,
                    row.Location == null ? null : row.Location.Code,
                    row.Department == null ? null : row.Department.Code,
                    row.Person == null ? null : row.Person.ExternalId,
                    row.Asset.PurchaseDate,
                    row.Asset.PurchaseCost,
                    row.Asset.Currency,
                    row.Asset.Supplier,
                    row.Warranty == null ? null : row.Warranty.StartDate,
                    row.Warranty == null ? null : row.Warranty.EndDate,
                    row.Warranty == null ? null : row.Warranty.Provider,
                    row.Warranty == null ? null : row.Warranty.Reference,
                    row.Asset.OperatingSystem,
                    row.Asset.OperatingSystemVersion,
                    row.Asset.Windows11Compliant,
                    row.Asset.LegacyId,
                    row.Asset.Notes,
                    row.Asset.ReportedSerialNumber))
            .ToArrayAsync(cancellationToken);
    }

    public void AddImportJob(AssetImportJob job) =>
        _dbContext.AssetImportJobs.Add(job);

    public void AddCategory(AssetCategory category) =>
        _dbContext.AssetCategories.Add(category);

    public void AddLocation(Inventory.Domain.Locations.Location location) =>
        _dbContext.Locations.Add(location);

    public void AddAsset(Asset asset) => _dbContext.Assets.Add(asset);

    public void AddAssignment(AssetAssignment assignment) =>
        _dbContext.AssetAssignments.Add(assignment);

    public void AddWarranty(WarrantyCoverage warranty) =>
        _dbContext.WarrantyCoverages.Add(warranty);

    public void AddAssetEvent(AssetEvent assetEvent) =>
        _dbContext.AssetEvents.Add(assetEvent);

    public void AddAuditLog(AuditLog auditLog) =>
        _dbContext.AuditLogs.Add(auditLog);

    public async Task SaveRejectedCommitAuditAfterFailureAsync(
        AuditLog auditLog,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        _dbContext.AuditLogs.Add(auditLog);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (PersistenceExceptionMapping.IsConstraintViolation(exception))
        {
            throw new PersistenceConflictException(
                "import_rejection_audit_failed",
                "The rejected import commit could not be recorded.",
                exception);
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesWithConflictMappingAsync(
            "import_data_conflict",
            "The import conflicts with current inventory data.",
            "The import or one of its referenced records changed.",
            cancellationToken);

    private async Task<Asset[]> QueryAssetsByIdentifiers(
        IReadOnlyCollection<string> assetTags,
        IReadOnlyCollection<string> serialNumbers,
        CancellationToken cancellationToken)
    {
        if (assetTags.Count == 0 && serialNumbers.Count == 0)
        {
            return [];
        }

        return await _dbContext.Assets
            .AsNoTracking()
            .Where(
                asset =>
                    assetTags.Contains(asset.AssetTagNormalized) ||
                    (asset.SerialNumberNormalized != null &&
                     serialNumbers.Contains(asset.SerialNumberNormalized)))
            .ToArrayAsync(cancellationToken);
    }

    private Task<Asset[]> QueryAssetsForCommitAsync(
        ImportCommitLookupRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AssetIds.Count == 0 &&
            request.AssetTags.Count == 0 &&
            request.SerialNumbers.Count == 0)
        {
            return Task.FromResult(Array.Empty<Asset>());
        }

        return _dbContext.Assets
            .Where(
                asset =>
                    request.AssetIds.Contains(asset.Id) ||
                    request.AssetTags.Contains(asset.AssetTagNormalized) ||
                    (asset.SerialNumberNormalized != null &&
                     request.SerialNumbers.Contains(asset.SerialNumberNormalized)))
            .ToArrayAsync(cancellationToken);
    }
}
