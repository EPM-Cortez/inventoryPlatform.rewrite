using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Application.SavedSearches;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;

namespace Inventory.Application.Tests;

public sealed class SavedSearchServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SavedSearchesAreOwnerScoped()
    {
        var repository = new FakeRepository();
        var other = SavedAssetSearch.Create(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Other user's search",
            1,
            FilterJson(),
            Now);
        repository.Items.Add(other.Id, other);
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => service.GetAsync(other.Id, CancellationToken.None));

        Assert.Equal("not_found", exception.Code);
    }

    [Fact]
    public async Task SettingDefaultAtomicallyClearsPreviousDefault()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);
        var first = await service.CreateAsync(
            new CreateSavedAssetSearchCommand("First", 1, ValidFilter()),
            CancellationToken.None);
        var second = await service.CreateAsync(
            new CreateSavedAssetSearchCommand("Second", 1, ValidFilter()),
            CancellationToken.None);

        first = await service.SetDefaultAsync(
            first.Id,
            new ChangeSavedAssetSearchCommand(first.Version),
            CancellationToken.None);
        second = await service.SetDefaultAsync(
            second.Id,
            new ChangeSavedAssetSearchCommand(second.Version),
            CancellationToken.None);

        Assert.False(repository.Items[first.Id].IsDefault);
        Assert.True(repository.Items[second.Id].IsDefault);
        Assert.Equal(5, repository.SaveCount);
    }

    [Fact]
    public async Task UserCannotCreateMoreThanFiftyActiveSearches()
    {
        var repository = new FakeRepository();
        for (var index = 0; index < SavedSearchService.MaximumSavedSearchesPerUser; index++)
        {
            var item = SavedAssetSearch.Create(
                UserId,
                $"Search {index}",
                1,
                FilterJson(),
                Now);
            repository.Items.Add(item.Id, item);
        }

        var service = CreateService(repository);
        var exception = await Assert.ThrowsAsync<ResourceConflictException>(
            () => service.CreateAsync(
                new CreateSavedAssetSearchCommand("One too many", 1, ValidFilter()),
                CancellationToken.None));

        Assert.Equal("saved_search_limit_reached", exception.Code);
        Assert.Equal(0, repository.SaveCount);
    }

    private static SavedSearchService CreateService(FakeRepository repository) =>
        new(
            repository,
            new FakeClock(),
            new FakeCurrentUserContext());

    private static SavedAssetFilter ValidFilter() =>
        new(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            50,
            "assetTag");

    private static string FilterJson() =>
        """
        {"query":null,"assetTag":null,"serialNumber":null,"categoryId":null,
        "status":null,"condition":null,"locationId":null,"departmentId":null,
        "assignedPersonId":null,"assigned":null,"warrantyState":null,
        "warrantyEndsBefore":null,"createdFrom":null,"createdTo":null,
        "legacyId":null,"pageSize":50,"sort":"assetTag"}
        """;

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Now;

        public DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public bool IsAuthenticated => true;

        public IReadOnlyCollection<string> Roles => ["Technician"];

        public IReadOnlyCollection<string> Permissions => ["assets.read"];

        public CurrentActor RequireActor() =>
            new(UserId, "Technician", "127.0.0.1", "test", "saved-search-test");
    }

    private sealed class FakeRepository : ISavedSearchRepository
    {
        public Dictionary<Guid, SavedAssetSearch> Items { get; } = [];

        public List<AuditLog> AuditLogs { get; } = [];

        public int SaveCount { get; private set; }

        public Task<ISavedSearchTransaction> BeginTransactionAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<ISavedSearchTransaction>(
                new FakeSavedSearchTransaction());

        public Task<SavedAssetSearch?> GetAsync(
            Guid userId,
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Items.GetValueOrDefault(id) is { } item &&
                item.UserId == userId &&
                item.ArchivedAtUtc is null
                    ? item
                    : null);

        public Task<SavedAssetSearch?> GetDefaultAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Items.Values.SingleOrDefault(
                    item =>
                        item.UserId == userId &&
                        item.ArchivedAtUtc is null &&
                        item.IsDefault));

        public async Task<SavedAssetSearchRecord?> GetRecordAsync(
            Guid userId,
            Guid id,
            CancellationToken cancellationToken) =>
            ToRecord(await GetAsync(userId, id, cancellationToken));

        public async Task<SavedAssetSearchRecord?> GetDefaultRecordAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            ToRecord(await GetDefaultAsync(userId, cancellationToken));

        public Task<PageResult<SavedAssetSearchRecord>> ListAsync(
            Guid userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            var records = Items.Values
                .Where(
                    item =>
                        item.UserId == userId &&
                        item.ArchivedAtUtc is null)
                .Select(item => ToRecord(item)!)
                .ToArray();
            return Task.FromResult(
                new PageResult<SavedAssetSearchRecord>(
                    records,
                    page,
                    pageSize,
                    records.Length));
        }

        public Task<int> CountActiveAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Items.Values.Count(
                    item =>
                        item.UserId == userId &&
                        item.ArchivedAtUtc is null));

        public Task<bool> NameExistsAsync(
            Guid userId,
            string normalizedName,
            Guid? excludingId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Items.Values.Any(
                    item =>
                        item.UserId == userId &&
                        item.ArchivedAtUtc is null &&
                        item.NameNormalized == normalizedName &&
                        (!excludingId.HasValue || item.Id != excludingId.Value)));

        public void Add(SavedAssetSearch savedSearch) =>
            Items.Add(savedSearch.Id, savedSearch);

        public void AddAuditLog(AuditLog auditLog) => AuditLogs.Add(auditLog);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        private static SavedAssetSearchRecord? ToRecord(SavedAssetSearch? item) =>
            item is null
                ? null
                : new SavedAssetSearchRecord(
                    item.Id,
                    item.Name,
                    item.SchemaVersion,
                    item.FilterJson,
                    item.IsDefault,
                    item.CreatedAtUtc,
                    item.UpdatedAtUtc,
                    item.Version);

        private sealed class FakeSavedSearchTransaction : ISavedSearchTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public Task RollbackAsync(CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
