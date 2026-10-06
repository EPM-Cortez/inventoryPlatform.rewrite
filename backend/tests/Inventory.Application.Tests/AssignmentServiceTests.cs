using Inventory.Application.Abstractions;
using Inventory.Application.Assets;
using Inventory.Application.Assignments;
using Inventory.Application.Common;
using Inventory.Application.People;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Auditing;
using Inventory.Domain.Locations;
using Inventory.Domain.People;

namespace Inventory.Application.Tests;

public sealed class AssignmentServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AssignTransferAndReturnEachUseOneAtomicSave()
    {
        var store = new FakeStore();
        var clock = new FakeClock(Now);
        var service = new AssignmentService(
            store,
            store,
            clock,
            new FakeCurrentUserContext(),
            new FakeSchoolSettingsProvider());
        var asset = Asset.Create(ValidAssetDetails(), Now);
        var person = Person.Create("STF-1", "A. Patel", PersonType.Staff);
        var department = Department.Create("ICT", "ICT Services");
        store.Assets.Add(asset.Id, asset);
        store.People.Add(person.Id, person);
        store.Departments.Add(department.Id, department);

        var assigned = await service.AssignOrTransferAsync(
            asset.Id,
            new AssignAssetCommand(person.Id, null, asset.Version, "Initial issue"),
            CancellationToken.None);

        Assert.Equal(1, store.SaveCount);
        Assert.Equal(AssetStatus.Assigned, asset.Status);
        Assert.Equal(AssignmentTargetType.Person, assigned.Assignment.TargetType);
        Assert.Single(store.Assignments.Values, assignment => assignment.IsOpen);

        clock.UtcNow = Now.AddDays(1);
        var transferred = await service.AssignOrTransferAsync(
            asset.Id,
            new AssignAssetCommand(
                null,
                department.Id,
                assigned.AssetVersion,
                "Department pool"),
            CancellationToken.None);

        Assert.Equal(2, store.SaveCount);
        Assert.Equal(AssignmentTargetType.Department, transferred.Assignment.TargetType);
        Assert.Equal(2, store.Assignments.Count);
        Assert.Single(store.Assignments.Values, assignment => assignment.IsOpen);
        Assert.Contains(store.Events, item => item.Type == AssetEventType.Transferred);

        clock.UtcNow = Now.AddDays(2);
        var returned = await service.ReturnAsync(
            asset.Id,
            transferred.Assignment.Id,
            new ReturnAssetCommand(
                transferred.AssetVersion,
                transferred.Assignment.Version,
                AssetCondition.Damaged,
                null,
                "Cracked screen"),
            CancellationToken.None);

        Assert.Equal(3, store.SaveCount);
        Assert.Equal(AssetStatus.InStock, returned.AssetStatus);
        Assert.Equal(AssetCondition.Damaged, returned.AssetCondition);
        Assert.DoesNotContain(store.Assignments.Values, assignment => assignment.IsOpen);
        Assert.Equal(
            [AssetEventType.Assigned, AssetEventType.Transferred, AssetEventType.Returned],
            store.Events.Select(item => item.Type).ToArray());
    }

    [Fact]
    public async Task StaleTransferDoesNotCloseCurrentAssignmentOrSave()
    {
        var store = new FakeStore();
        var service = new AssignmentService(
            store,
            store,
            new FakeClock(Now),
            new FakeCurrentUserContext(),
            new FakeSchoolSettingsProvider());
        var asset = Asset.Create(ValidAssetDetails(), Now);
        var person = Person.Create("STF-1", "A. Patel", PersonType.Staff);
        var department = Department.Create("ICT", "ICT Services");
        store.Assets.Add(asset.Id, asset);
        store.People.Add(person.Id, person);
        store.Departments.Add(department.Id, department);

        await service.AssignOrTransferAsync(
            asset.Id,
            new AssignAssetCommand(person.Id, null, asset.Version, null),
            CancellationToken.None);
        var saveCount = store.SaveCount;
        var current = Assert.Single(
            store.Assignments.Values,
            assignment => assignment.IsOpen);

        var exception = await Assert.ThrowsAsync<ResourceConflictException>(
            () => service.AssignOrTransferAsync(
                asset.Id,
                new AssignAssetCommand(null, department.Id, Guid.NewGuid(), null),
                CancellationToken.None));

        Assert.Equal("concurrency_conflict", exception.Code);
        Assert.Equal(saveCount, store.SaveCount);
        Assert.True(current.IsOpen);
    }

    [Fact]
    public async Task ExpectedReturnUpdateIsAtomicAndAudited()
    {
        var store = new FakeStore();
        var service = new AssignmentService(
            store,
            store,
            new FakeClock(Now),
            new FakeCurrentUserContext(),
            new FakeSchoolSettingsProvider());
        var asset = Asset.Create(ValidAssetDetails(), Now);
        var person = Person.Create("STF-1", "A. Patel", PersonType.Staff);
        store.Assets.Add(asset.Id, asset);
        store.People.Add(person.Id, person);
        var assigned = await service.AssignOrTransferAsync(
            asset.Id,
            new AssignAssetCommand(
                person.Id,
                null,
                asset.Version,
                null,
                new DateOnly(2026, 7, 30)),
            CancellationToken.None);

        var result = await service.UpdateExpectedReturnAsync(
            asset.Id,
            assigned.Assignment.Id,
            new UpdateExpectedReturnCommand(
                new DateOnly(2026, 8, 1),
                assigned.Assignment.Version),
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 8, 1), result.ExpectedReturnOn);
        Assert.Equal(2, store.SaveCount);
        Assert.Contains(
            store.Events,
            item => item.Type == AssetEventType.ExpectedReturnChanged);
        Assert.Contains(
            store.AuditLogs,
            item => item.Action == "assignment.expected_return.updated");
    }

    private static AssetDetails ValidAssetDetails() =>
        new(
            "SCH-001",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Dell",
            "Latitude",
            "SERIAL-001",
            AssetStatus.InStock,
            AssetCondition.Good,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }

        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public bool IsAuthenticated => true;

        public IReadOnlyCollection<string> Roles => ["Technician"];

        public IReadOnlyCollection<string> Permissions => [];

        public CurrentActor RequireActor() =>
            new(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "Technician",
                "127.0.0.1",
                "test",
                "assignment-test");
    }

    private sealed class FakeSchoolSettingsProvider : ISchoolSettingsProvider
    {
        public Task<SchoolSettingsSnapshot> GetAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new SchoolSettingsSnapshot(
                    "Test School",
                    "TST",
                    "Europe/London",
                    "https://inventory.test/qr",
                    30));
    }

    private sealed class FakeStore : IInventoryRepository, ICustodyRepository
    {
        public Dictionary<Guid, Asset> Assets { get; } = [];

        public Dictionary<Guid, Person> People { get; } = [];

        public Dictionary<Guid, Department> Departments { get; } = [];

        public Dictionary<Guid, AssetAssignment> Assignments { get; } = [];

        public Dictionary<Guid, Location> Locations { get; } = [];

        public List<AssetEvent> Events { get; } = [];

        public List<AuditLog> AuditLogs { get; } = [];

        public int SaveCount { get; private set; }

        public Task<Asset?> GetAssetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Assets.GetValueOrDefault(id));

        public Task<AssetView?> GetAssetViewAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Assets.TryGetValue(id, out var asset)
                    ? ToAssetView(asset)
                    : null);

        public Task<AssetView?> GetAssetViewByTagAsync(
            string normalizedAssetTag,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PageResult<AssetListItem>> SearchAssetsAsync(
            AssetSearch search,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> AssetTagExistsAsync(
            string normalizedAssetTag,
            Guid? excludingAssetId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SerialNumberExistsAsync(
            string normalizedSerialNumber,
            Guid? excludingAssetId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid?> GetLinkedManagedAgentIdAsync(
            Guid assetId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(null);

        public Task<AssetCategory?> GetCategoryAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> CategoryCodeExistsAsync(
            string normalizedCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AssetCategory>> ListCategoriesAsync(
            bool includeInactive,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Location?> GetLocationAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Locations.GetValueOrDefault(id));

        public Task<bool> LocationCodeExistsAsync(
            string normalizedCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Location>> ListLocationsAsync(
            bool includeInactive,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PageResult<AssetEvent>> GetAssetHistoryAsync(
            Guid assetId,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void AddAsset(Asset asset) => Assets.Add(asset.Id, asset);

        public void AddAssetEvent(AssetEvent assetEvent) => Events.Add(assetEvent);

        public void AddCategory(AssetCategory category) => throw new NotSupportedException();

        public void AddLocation(Location location) => Locations.Add(location.Id, location);

        public void AddAuditLog(AuditLog auditLog) => AuditLogs.Add(auditLog);

        public Task<Person?> GetPersonAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(People.GetValueOrDefault(id));

        public Task<PersonView?> GetPersonViewAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PageResult<PersonView>> SearchPeopleAsync(
            PersonSearch search,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> PersonExternalIdExistsAsync(
            string normalizedExternalId,
            Guid? excludingPersonId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> PersonHasOpenAssignmentsAsync(
            Guid personId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> PersonHasBlockingLeaverCaseAsync(
            Guid personId,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<Department?> GetDepartmentAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(Departments.GetValueOrDefault(id));

        public Task<DepartmentView?> GetDepartmentViewAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PageResult<DepartmentView>> SearchDepartmentsAsync(
            DepartmentSearch search,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DepartmentCodeExistsAsync(
            string normalizedCode,
            Guid? excludingDepartmentId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DepartmentHasOpenAssignmentsAsync(
            Guid departmentId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AssetAssignment?> GetAssignmentAsync(
            Guid assetId,
            Guid assignmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Assignments.TryGetValue(assignmentId, out var assignment) &&
                assignment.AssetId == assetId
                    ? assignment
                    : null);

        public Task<AssetAssignment?> GetOpenAssignmentAsync(
            Guid assetId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Assignments.Values.SingleOrDefault(
                    assignment => assignment.AssetId == assetId && assignment.IsOpen));

        public Task<AssetAssignmentView?> GetAssignmentViewAsync(
            Guid assignmentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Assignments.TryGetValue(assignmentId, out var assignment)
                    ? ToAssignmentView(assignment)
                    : null);

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

        public void AddPerson(Person person) => People.Add(person.Id, person);

        public void AddDepartment(Department department) =>
            Departments.Add(department.Id, department);

        public void AddAssignment(AssetAssignment assignment) =>
            Assignments.Add(assignment.Id, assignment);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        private AssetView ToAssetView(Asset asset)
        {
            var location = asset.LocationId.HasValue
                ? Locations.GetValueOrDefault(asset.LocationId.Value)
                : null;
            var assignment = Assignments.Values.SingleOrDefault(
                item => item.AssetId == asset.Id && item.IsOpen);
            return new AssetView(
                asset.Id,
                asset.AssetTag,
                asset.DeviceName,
                asset.CategoryId,
                "CAT",
                "Category",
                asset.Description,
                asset.Manufacturer,
                asset.Model,
                asset.SerialNumber,
                asset.LanMacAddress,
                asset.BiosGuid,
                asset.Ownership,
                asset.Status,
                asset.Condition,
                asset.LocationId,
                location?.Code,
                location?.Name,
                assignment is null
                    ? null
                    : new CurrentAssignmentView(
                        assignment.Id,
                        assignment.TargetType,
                        assignment.PersonId,
                        assignment.DepartmentId,
                        assignment.TargetDisplayName,
                        assignment.TargetReference,
                        assignment.AssignedAtUtc,
                        assignment.Version),
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

        private static AssetAssignmentView ToAssignmentView(AssetAssignment assignment) =>
            new(
                assignment.Id,
                assignment.AssetId,
                assignment.TargetType,
                assignment.PersonId,
                assignment.DepartmentId,
                assignment.TargetDisplayName,
                assignment.TargetReference,
                assignment.AssignedAtUtc,
                assignment.AssignedByUserId,
                assignment.AssignedByDisplayName,
                assignment.AssignedCondition,
                assignment.AssignedLocationId,
                null,
                null,
                assignment.AssignmentNotes,
                assignment.ReturnedAtUtc,
                assignment.ReturnedByUserId,
                assignment.ReturnedByDisplayName,
                assignment.ReturnCondition,
                assignment.ReturnLocationId,
                null,
                null,
                assignment.ReturnNotes,
                assignment.Version,
                assignment.ExpectedReturnOn,
                false);
    }
}
