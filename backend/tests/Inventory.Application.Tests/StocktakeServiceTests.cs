using Inventory.Application.Common;
using Inventory.Application.Abstractions;
using Inventory.Application.Stocktakes;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Inventory.Domain.Stocktakes;

namespace Inventory.Application.Tests;

public sealed class StocktakeServiceTests
{
    private static readonly Guid ActorId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CategoryId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FirstLocationId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondLocationId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartCapturesAnImmutableExpectedAssetSnapshot()
    {
        var source = Source("AST-001", "Keyboard", FirstLocationId, "Library");
        var repository = CreateRepository(source);
        var service = CreateService(repository);

        var session = await service.StartAsync(
            new StartStocktakeCommand(
                "Summer stocktake",
                StocktakeScope.All,
                null,
                null),
            CancellationToken.None);
        repository.Sources[0] = source with
        {
            AssetTag = "AST-CHANGED",
            Condition = AssetCondition.Damaged,
            LocationName = "Moved",
        };

        var expected = Assert.Single(repository.ExpectedAssets);
        Assert.Equal("AST-001", expected.AssetTag);
        Assert.Equal(AssetCondition.Good, expected.ExpectedCondition);
        Assert.Equal("Library", expected.ExpectedLocationName);
        Assert.Equal(1, session.Progress.Expected);
    }

    [Fact]
    public async Task ExactRescanWithStaleVersionDoesNotDuplicateOrAdvanceSession()
    {
        var source = Source("AST-001", "Keyboard", FirstLocationId, "Library");
        var repository = CreateRepository(source);
        var service = CreateService(repository);
        var started = await service.StartAsync(
            new StartStocktakeCommand("Summer", StocktakeScope.All, null, null),
            CancellationToken.None);
        var command = new RecordStocktakeObservationCommand(
            source.AssetId,
            null,
            FirstLocationId,
            AssetCondition.Good,
            "Present",
            started.Version);

        var first = await service.RecordObservationAsync(
            started.Id,
            command,
            CancellationToken.None);
        var retry = await service.RecordObservationAsync(
            started.Id,
            command,
            CancellationToken.None);

        Assert.True(first.Changed);
        Assert.False(retry.Changed);
        Assert.Equal(first.Session.Version, retry.Session.Version);
        Assert.Single(repository.Observations);
        Assert.Single(
            repository.AuditLogs,
            audit => audit.Action == "stocktake.asset.observed");
    }

    [Fact]
    public async Task ProgressSeparatesMissingUnexpectedAndRegisterDisagreements()
    {
        var expectedOne = Source("AST-001", "Keyboard", FirstLocationId, "Library");
        var expectedTwo = Source("AST-002", "Keyboard", FirstLocationId, "Library");
        var outsideScope = Source("AST-003", "Keyboard", SecondLocationId, "Office");
        var repository = CreateRepository(expectedOne, expectedTwo, outsideScope);
        var service = CreateService(repository);
        var started = await service.StartAsync(
            new StartStocktakeCommand(
                "Library",
                StocktakeScope.Location,
                FirstLocationId,
                null),
            CancellationToken.None);

        var first = await service.RecordObservationAsync(
            started.Id,
            new RecordStocktakeObservationCommand(
                expectedOne.AssetId,
                null,
                SecondLocationId,
                AssetCondition.Damaged,
                null,
                started.Version),
            CancellationToken.None);
        var second = await service.RecordObservationAsync(
            started.Id,
            new RecordStocktakeObservationCommand(
                outsideScope.AssetId,
                null,
                SecondLocationId,
                AssetCondition.Good,
                null,
                first.Session.Version),
            CancellationToken.None);

        Assert.Equal(
            new StocktakeProgress(
                Expected: 2,
                Seen: 1,
                Missing: 1,
                Unexpected: 1,
                WrongLocation: 1,
                ConditionChanged: 1),
            second.Session.Progress);
        Assert.Equal(AssetCondition.Good, expectedOne.Condition);
        Assert.Equal(FirstLocationId, expectedOne.LocationId);
    }

    private static FakeStocktakeRepository CreateRepository(
        params StocktakeAssetSnapshotSource[] sources)
    {
        var repository = new FakeStocktakeRepository(sources);
        repository.Locations[FirstLocationId] =
            new StocktakeReferenceView(FirstLocationId, "LIB", "Library", true);
        repository.Locations[SecondLocationId] =
            new StocktakeReferenceView(SecondLocationId, "OFF", "Office", true);
        repository.Categories[CategoryId] =
            new StocktakeReferenceView(CategoryId, "KEY", "Keyboard", true);
        return repository;
    }

    private static StocktakeService CreateService(FakeStocktakeRepository repository) =>
        new(
            repository,
            new FakeClock(),
            new FakeCurrentUser(),
            new AllowStorageWriteGuard());

    private static StocktakeAssetSnapshotSource Source(
        string assetTag,
        string categoryName,
        Guid locationId,
        string locationName) =>
        new(
            Guid.NewGuid(),
            assetTag,
            $"{locationName} {categoryName}",
            CategoryId,
            "KEY",
            categoryName,
            AssetStatus.InStock,
            AssetCondition.Good,
            locationId,
            locationName[..Math.Min(locationName.Length, 3)].ToUpperInvariant(),
            locationName);

    private sealed class FakeStocktakeRepository : IStocktakeRepository
    {
        public FakeStocktakeRepository(IEnumerable<StocktakeAssetSnapshotSource> sources)
        {
            Sources.AddRange(sources);
        }

        public List<StocktakeAssetSnapshotSource> Sources { get; } = [];

        public Dictionary<Guid, StocktakeReferenceView> Locations { get; } = [];

        public Dictionary<Guid, StocktakeReferenceView> Categories { get; } = [];

        public List<StocktakeExpectedAsset> ExpectedAssets { get; } = [];

        public List<StocktakeObservation> Observations { get; } = [];

        public List<AuditLog> AuditLogs { get; } = [];

        private List<StocktakeSession> Sessions { get; } = [];

        public Task<IStocktakeTransaction> BeginTransactionAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IStocktakeTransaction>(new FakeStocktakeTransaction());

        public Task<StocktakeSession?> GetSessionAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.SingleOrDefault(session => session.Id == sessionId));

        public Task<int> CountInProgressSessionsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Sessions.Count(
                    session => session.Status == StocktakeStatus.InProgress));

        public Task<bool> HasMatchingInProgressSessionAsync(
            string normalizedName,
            StocktakeScope scope,
            Guid? scopeLocationId,
            Guid? scopeCategoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Sessions.Any(
                    session =>
                        session.Status == StocktakeStatus.InProgress &&
                        string.Equals(
                            session.Name,
                            normalizedName,
                            StringComparison.OrdinalIgnoreCase) &&
                        session.Scope == scope &&
                        session.ScopeLocationId == scopeLocationId &&
                        session.ScopeCategoryId == scopeCategoryId));

        public Task<PageResult<StocktakeSessionListItem>> SearchSessionsAsync(
            StocktakeSessionSearch search,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StocktakeReferenceView?> GetLocationAsync(
            Guid locationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Locations.GetValueOrDefault(locationId));

        public Task<StocktakeReferenceView?> GetCategoryAsync(
            Guid categoryId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Categories.GetValueOrDefault(categoryId));

        public Task<IReadOnlyList<StocktakeAssetSnapshotSource>> ListSnapshotSourcesAsync(
            StocktakeScope scope,
            Guid? scopeLocationId,
            Guid? scopeCategoryId,
            int maximumResults,
            CancellationToken cancellationToken)
        {
            IEnumerable<StocktakeAssetSnapshotSource> query = Sources.Where(
                source => source.Status is not (AssetStatus.Retired or AssetStatus.Disposed));
            query = scope switch
            {
                StocktakeScope.Location =>
                    query.Where(source => source.LocationId == scopeLocationId),
                StocktakeScope.Category =>
                    query.Where(source => source.CategoryId == scopeCategoryId),
                _ => query,
            };
            return Task.FromResult<IReadOnlyList<StocktakeAssetSnapshotSource>>(
                query.Take(maximumResults).ToArray());
        }

        public Task<StocktakeAssetSnapshotSource?> FindAssetAsync(
            Guid? assetId,
            string? normalizedAssetTag,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Sources.SingleOrDefault(
                    source => assetId.HasValue
                        ? source.AssetId == assetId.Value
                        : string.Equals(
                            source.AssetTag,
                            normalizedAssetTag,
                            StringComparison.OrdinalIgnoreCase)));

        public Task<StocktakeObservation?> GetObservationAsync(
            Guid sessionId,
            Guid assetId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Observations.SingleOrDefault(
                    observation =>
                        observation.SessionId == sessionId &&
                        observation.AssetId == assetId));

        public Task<StocktakeProgress> GetProgressAsync(
            Guid sessionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(CalculateProgress(sessionId));

        public Task<PageResult<StocktakeItemView>> GetItemsAsync(
            Guid sessionId,
            StocktakeItemSearch search,
            CancellationToken cancellationToken)
        {
            var items = BuildItems(sessionId)
                .Where(
                    item => search.Filter switch
                    {
                        StocktakeItemFilter.Seen => item.IsExpected && item.IsSeen,
                        StocktakeItemFilter.Missing => item.IsExpected && !item.IsSeen,
                        StocktakeItemFilter.Unexpected => item.IsUnexpected,
                        StocktakeItemFilter.WrongLocation => item.IsWrongLocation,
                        StocktakeItemFilter.ConditionChanged => item.IsConditionChanged,
                        _ => true,
                    })
                .OrderBy(item => item.AssetTag)
                .ToArray();
            return Task.FromResult(
                new PageResult<StocktakeItemView>(
                    items
                        .Skip((search.Page - 1) * search.PageSize)
                        .Take(search.PageSize)
                        .ToArray(),
                    search.Page,
                    search.PageSize,
                    items.LongLength));
        }

        public Task<StocktakeItemView?> GetItemAsync(
            Guid sessionId,
            Guid assetId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                BuildItems(sessionId).SingleOrDefault(item => item.AssetId == assetId));

        public void AddSession(StocktakeSession session) => Sessions.Add(session);

        public void AddExpectedAssets(IEnumerable<StocktakeExpectedAsset> expectedAssets) =>
            ExpectedAssets.AddRange(expectedAssets);

        public void AddObservation(StocktakeObservation observation) =>
            Observations.Add(observation);

        public void AddAudit(AuditLog auditLog) => AuditLogs.Add(auditLog);

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        private StocktakeProgress CalculateProgress(Guid sessionId)
        {
            var items = BuildItems(sessionId);
            return new StocktakeProgress(
                items.LongCount(item => item.IsExpected),
                items.LongCount(item => item.IsExpected && item.IsSeen),
                items.LongCount(item => item.IsExpected && !item.IsSeen),
                items.LongCount(item => item.IsUnexpected),
                items.LongCount(item => item.IsWrongLocation),
                items.LongCount(item => item.IsConditionChanged));
        }

        private StocktakeItemView[] BuildItems(Guid sessionId)
        {
            var expected = ExpectedAssets
                .Where(item => item.SessionId == sessionId)
                .Select(
                    item =>
                    {
                        var observation = Observations.SingleOrDefault(
                            found =>
                                found.SessionId == sessionId &&
                                found.AssetId == item.AssetId);
                        return CreateItem(item, observation);
                    });
            var unexpected = Observations
                .Where(
                    observation =>
                        observation.SessionId == sessionId &&
                        ExpectedAssets.All(
                            item =>
                                item.SessionId != sessionId ||
                                item.AssetId != observation.AssetId))
                .Select(observation => CreateItem(null, observation));
            return expected.Concat(unexpected).ToArray();
        }

        private static StocktakeItemView CreateItem(
            StocktakeExpectedAsset? expected,
            StocktakeObservation? observation)
        {
            var assetId = expected?.AssetId ?? observation!.AssetId;
            return new StocktakeItemView(
                assetId,
                expected?.AssetTag ?? observation!.AssetTag,
                expected?.DeviceName ?? observation!.DeviceName,
                expected?.CategoryId ?? observation!.CategoryId,
                expected?.CategoryCode ?? observation!.CategoryCode,
                expected?.CategoryName ?? observation!.CategoryName,
                expected is not null,
                observation is not null,
                expected?.ExpectedStatus,
                expected?.ExpectedCondition,
                expected?.ExpectedLocationId,
                expected?.ExpectedLocationCode,
                expected?.ExpectedLocationName,
                observation?.ObservedLocationId,
                observation?.ObservedLocationCode,
                observation?.ObservedLocationName,
                observation?.ObservedCondition,
                observation?.Note,
                observation?.ObservedAtUtc,
                observation?.ObservedByUserId,
                observation?.ObservedByDisplayName,
                expected is null,
                expected is not null &&
                observation is not null &&
                expected.ExpectedLocationId != observation.ObservedLocationId,
                expected is not null &&
                observation is not null &&
                expected.ExpectedCondition != observation.ObservedCondition);
        }

        private sealed class FakeStocktakeTransaction : IStocktakeTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public Task RollbackAsync(CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Now;

        public DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);
    }

    private sealed class AllowStorageWriteGuard : IDatabaseStorageWriteGuard
    {
        public void EnsureCapacity(long additionalBytes)
        {
        }
    }

    private sealed class FakeCurrentUser : ICurrentUserContext
    {
        public bool IsAuthenticated => true;

        public IReadOnlyCollection<string> Roles => [];

        public IReadOnlyCollection<string> Permissions => [];

        public CurrentActor RequireActor() =>
            new(ActorId, "Technician", "127.0.0.1", "tests", "stocktake-test");
    }
}
