using Inventory.Application.Abstractions;
using Inventory.Application.Assets;
using Inventory.Application.AssetTags;
using Inventory.Application.Assignments;
using Inventory.Application.Bundles;
using Inventory.Application.Common;
using Inventory.Application.People;
using Inventory.Domain.AssetTags;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Auditing;
using Inventory.Domain.BulkChanges;
using Inventory.Domain.Bundles;
using Inventory.Domain.Locations;
using Inventory.Domain.People;

namespace Inventory.Application.Tests;

public sealed class AssetServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAddsAssetAndHistoryInOneSave()
    {
        var repository = new FakeInventoryRepository();
        var category = AssetCategory.Create("LAP", "Laptop");
        repository.Categories.Add(category.Id, category);
        var service = CreateService(repository);

        var result = await service.CreateAsync(
            ValidCreate(category.Id),
            CancellationToken.None);

        Assert.Equal("SCH-001", result.AssetTag);
        Assert.Single(repository.Assets);
        var assetEvent = Assert.Single(repository.AssetEvents);
        Assert.Equal(AssetEventType.AssetCreated, assetEvent.Type);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task CreateRejectsCaseInsensitiveDuplicateTag()
    {
        var repository = new FakeInventoryRepository();
        var category = AssetCategory.Create("LAP", "Laptop");
        repository.Categories.Add(category.Id, category);
        var service = CreateService(repository);

        await service.CreateAsync(
            ValidCreate(category.Id),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ResourceConflictException>(
            () => service.CreateAsync(
                ValidCreate(category.Id) with
                {
                    AssetTag = "sch-001",
                    SerialNumber = "SERIAL-002",
                },
                CancellationToken.None));

        Assert.Equal("duplicate_asset_tag", exception.Code);
    }

    [Fact]
    public async Task UpdateRejectsStaleVersionWithoutSaving()
    {
        var repository = new FakeInventoryRepository();
        var category = AssetCategory.Create("LAP", "Laptop");
        repository.Categories.Add(category.Id, category);
        var service = CreateService(repository);
        var created = await service.CreateAsync(
            ValidCreate(category.Id),
            CancellationToken.None);
        var saveCountBeforeUpdate = repository.SaveCount;

        var exception = await Assert.ThrowsAsync<ResourceConflictException>(
            () => service.UpdateAsync(
                created.Id,
                new UpdateAssetCommand(
                    created.AssetTag,
                    created.DeviceName,
                    created.CategoryId,
                    created.Description,
                    created.Manufacturer,
                    "Changed",
                    created.SerialNumber,
                    created.LanMacAddress,
                    created.BiosGuid,
                    created.Ownership,
                    created.Status,
                    created.Condition,
                    created.LocationId,
                    created.PurchaseDate,
                    created.PurchaseCost,
                    created.Currency,
                    created.Supplier,
                    created.OperatingSystem,
                    created.OperatingSystemVersion,
                    created.Windows11Compliant,
                    created.Notes,
                    created.LegacyId,
                    Guid.NewGuid()),
                CancellationToken.None));

        Assert.Equal("concurrency_conflict", exception.Code);
        Assert.Equal(saveCountBeforeUpdate, repository.SaveCount);
    }

    private static AssetService CreateService(FakeInventoryRepository repository)
    {
        var operationsRepository = new EmptyAssetOperationsRepository();
        var clock = new FakeClock(Now);
        var currentUser = new FakeCurrentUserContext();
        var assetTagPolicyService = new AssetTagPolicyService(
            operationsRepository,
            repository,
            clock,
            currentUser);
        return new AssetService(
            repository,
            new EmptyCustodyRepository(),
            operationsRepository,
            assetTagPolicyService,
            clock,
            currentUser);
    }

    private static CreateAssetCommand ValidCreate(Guid categoryId) =>
        new(
            "SCH-001",
            "SCH-LAPTOP-001",
            categoryId,
            "Classroom laptop",
            "Dell",
            "Latitude",
            "SERIAL-001",
            "74:86:E2:3A:00:96",
            Guid.Parse("4C4C4544-0030-3310-8043-C3C04F475233"),
            "Leased",
            AssetStatus.InStock,
            AssetCondition.Good,
            null,
            new DateOnly(2026, 1, 1),
            1000m,
            "GBP",
            "Supplier",
            "Windows",
            "11",
            true,
            "Notes",
            "LEGACY-1");

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }

        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public bool IsAuthenticated => true;

        public IReadOnlyCollection<string> Roles => ["Administrator"];

        public IReadOnlyCollection<string> Permissions => [];

        public CurrentActor RequireActor() =>
            new(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Test Administrator",
                "127.0.0.1",
                "test",
                "test-correlation");
    }

    private sealed class FakeInventoryRepository : IInventoryRepository
    {
        public Dictionary<Guid, Asset> Assets { get; } = [];

        public Dictionary<Guid, AssetCategory> Categories { get; } = [];

        public Dictionary<Guid, Location> Locations { get; } = [];

        public List<AssetEvent> AssetEvents { get; } = [];

        public int SaveCount { get; private set; }

        public Task<Asset?> GetAssetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Assets.GetValueOrDefault(id));

        public Task<AssetView?> GetAssetViewAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Assets.TryGetValue(id, out var asset)
                    ? ToView(asset)
                    : null);

        public Task<AssetView?> GetAssetViewByTagAsync(
            string normalizedAssetTag,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Assets.Values
                    .Where(asset => asset.AssetTagNormalized == normalizedAssetTag)
                    .Select(ToView)
                    .SingleOrDefault());

        public Task<PageResult<AssetListItem>> SearchAssetsAsync(
            AssetSearch search,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> AssetTagExistsAsync(
            string normalizedAssetTag,
            Guid? excludingAssetId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Assets.Values.Any(
                    asset =>
                        asset.AssetTagNormalized == normalizedAssetTag &&
                        asset.Id != excludingAssetId));

        public Task<bool> SerialNumberExistsAsync(
            string normalizedSerialNumber,
            Guid? excludingAssetId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Assets.Values.Any(
                    asset =>
                        asset.SerialNumberNormalized == normalizedSerialNumber &&
                        asset.Id != excludingAssetId));

        public Task<Guid?> GetLinkedManagedAgentIdAsync(
            Guid assetId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);

        public Task<AssetCategory?> GetCategoryAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Categories.GetValueOrDefault(id));

        public Task<bool> CategoryCodeExistsAsync(
            string normalizedCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Categories.Values.Any(category => category.CodeNormalized == normalizedCode));

        public Task<IReadOnlyList<AssetCategory>> ListCategoriesAsync(
            bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssetCategory>>(
                Categories.Values
                    .Where(category => includeInactive || category.IsActive)
                    .ToArray());

        public Task<Location?> GetLocationAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Locations.GetValueOrDefault(id));

        public Task<bool> LocationCodeExistsAsync(
            string normalizedCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Locations.Values.Any(location => location.CodeNormalized == normalizedCode));

        public Task<IReadOnlyList<Location>> ListLocationsAsync(
            bool includeInactive,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Location>>(
                Locations.Values
                    .Where(location => includeInactive || location.IsActive)
                    .ToArray());

        public Task<PageResult<AssetEvent>> GetAssetHistoryAsync(
            Guid assetId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var matching = AssetEvents
                .Where(assetEvent => assetEvent.AssetId == assetId)
                .ToArray();
            return Task.FromResult(
                new PageResult<AssetEvent>(matching, page, pageSize, matching.LongLength));
        }

        public void AddAsset(Asset asset) => Assets.Add(asset.Id, asset);

        public void AddAssetEvent(AssetEvent assetEvent) => AssetEvents.Add(assetEvent);

        public void AddCategory(AssetCategory category) => Categories.Add(category.Id, category);

        public void AddLocation(Location location) => Locations.Add(location.Id, location);

        public void AddAuditLog(AuditLog auditLog)
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        private AssetView ToView(Asset asset)
        {
            var category = Categories[asset.CategoryId];
            var location = asset.LocationId.HasValue
                ? Locations[asset.LocationId.Value]
                : null;
            return new AssetView(
                asset.Id,
                asset.AssetTag,
                asset.DeviceName,
                category.Id,
                category.Code,
                category.Name,
                asset.Description,
                asset.Manufacturer,
                asset.Model,
                asset.SerialNumber,
                asset.LanMacAddress,
                asset.BiosGuid,
                asset.Ownership,
                asset.Status,
                asset.Condition,
                location?.Id,
                location?.Code,
                location?.Name,
                null,
                new WarrantySummaryView(
                    Inventory.Domain.Warranties.WarrantyState.NotRecorded,
                    null,
                    null,
                    null),
                asset.OperatingSystem,
                asset.OperatingSystemVersion,
                asset.Windows11Compliant,
                asset.PurchaseDate,
                asset.PurchaseCost,
                asset.Currency,
                asset.Supplier,
                asset.Notes,
                asset.LegacyId,
                asset.CreatedAtUtc,
                asset.UpdatedAtUtc,
                asset.RetiredAtUtc,
                asset.RetirementReason,
                asset.DisposedAtUtc,
                asset.DisposalReason,
                asset.Version);
        }
    }

    private sealed class EmptyAssetOperationsRepository
        : IAssetOperationsRepository
    {
        public Task<IAssetOperationsTransaction> BeginTransactionAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AssetTagPolicy?> GetAssetTagPolicyAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> ReserveNextAssetTagAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AssetBulkChangeJob?> GetBulkChangeJobForUpdateAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AssetBulkChangeJob?> GetReusableBulkChangeJobAsync(
            Guid createdByUserId,
            string fingerprint,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> CountActiveBulkChangeJobsAsync(
            Guid createdByUserId,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Asset>> GetAssetsForUpdateAsync(
            IReadOnlyCollection<Guid> assetIds,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AssetBundle?> GetBundleForUpdateAsync(
            Guid bundleId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AssetBundleView?> GetBundleViewAsync(
            Guid bundleId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PageResult<AssetBundleListItem>> SearchBundlesAsync(
            bool includeArchived,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> BundleCodeExistsAsync(
            string normalizedCode,
            Guid? excludingBundleId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> BundleNameExistsAsync(
            string normalizedName,
            Guid? excludingBundleId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlySet<Guid>> GetAssetsInOtherActiveBundlesAsync(
            IReadOnlyCollection<Guid> assetIds,
            Guid? excludingBundleId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(AssetBulkChangeJob job) =>
            throw new NotSupportedException();

        public void Add(AssetBundle bundle) =>
            throw new NotSupportedException();

        public void AddAssetEvent(AssetEvent assetEvent) =>
            throw new NotSupportedException();

        public void AddAuditLog(AuditLog auditLog) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyCustodyRepository : ICustodyRepository
    {
        public Task<Person?> GetPersonAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult<Person?>(null);

        public Task<PersonView?> GetPersonViewAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult<PersonView?>(null);

        public Task<PageResult<PersonView>> SearchPeopleAsync(
            PersonSearch search,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> PersonExternalIdExistsAsync(
            string normalizedExternalId,
            Guid? excludingPersonId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> PersonHasOpenAssignmentsAsync(
            Guid personId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> PersonHasBlockingLeaverCaseAsync(
            Guid personId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<Department?> GetDepartmentAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult<Department?>(null);

        public Task<DepartmentView?> GetDepartmentViewAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult<DepartmentView?>(null);

        public Task<PageResult<DepartmentView>> SearchDepartmentsAsync(
            DepartmentSearch search,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DepartmentCodeExistsAsync(
            string normalizedCode,
            Guid? excludingDepartmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> DepartmentHasOpenAssignmentsAsync(
            Guid departmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<AssetAssignment?> GetAssignmentAsync(
            Guid assetId,
            Guid assignmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<AssetAssignment?>(null);

        public Task<AssetAssignment?> GetOpenAssignmentAsync(
            Guid assetId,
            CancellationToken cancellationToken) =>
            Task.FromResult<AssetAssignment?>(null);

        public Task<AssetAssignmentView?> GetAssignmentViewAsync(
            Guid assignmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<AssetAssignmentView?>(null);

        public Task<PageResult<AssetAssignmentView>> GetAssetAssignmentsAsync(
            Guid assetId,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PageResult<HolderAssetView>> GetHolderAssetsAsync(
            Guid? personId,
            Guid? departmentId,
            bool includeHistory,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PageResult<OverdueLoanView>> SearchOverdueLoansAsync(
            DateOnly schoolToday,
            Guid? personId,
            Guid? departmentId,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddPerson(Person person) => throw new NotSupportedException();

        public void AddDepartment(Department department) => throw new NotSupportedException();

        public void AddAssignment(AssetAssignment assignment) =>
            throw new NotSupportedException();

        public void AddAuditLog(AuditLog auditLog) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
