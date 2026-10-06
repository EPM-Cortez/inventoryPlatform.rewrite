using Inventory.Application.Abstractions;
using Inventory.Application.Assets;
using Inventory.Application.Common;
using Inventory.Application.Qr;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Inventory.Domain.Locations;
using Inventory.Domain.Qr;

namespace Inventory.Application.Tests;

public sealed class QrServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ActorId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task RotateRevokesAndReplacesThroughTheAtomicRepositoryOperation()
    {
        var category = AssetCategory.Create("QR", "QR test");
        var asset = Asset.Create(
            new AssetDetails(
                "QR-001",
                category.Id,
                null,
                null,
                "SERIAL-PRIVATE",
                AssetStatus.InStock,
                AssetCondition.Good,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            Now);
        var inventory = new QrInventoryRepository(asset, category);
        var original = QrIdentifier.Issue(
            asset.Id,
            "hash-old",
            "protected-old",
            1,
            Now.AddMinutes(-1),
            ActorId,
            "Test Administrator");
        var qrRepository = new FakeQrRepository(original);
        var service = new QrService(
            inventory,
            qrRepository,
            new FakeTokenProtector(),
            new UnusedRenderer(),
            new FakeSchoolSettingsProvider(),
            new FakeClock(),
            new FakeCurrentUserContext());

        var result = await service.RotateAsync(asset.Id, CancellationToken.None);

        Assert.Equal(2, result.TokenVersion);
        Assert.False(original.IsActive);
        Assert.Equal(1, qrRepository.RotationSaveCount);
        Assert.Equal(0, qrRepository.NormalSaveCount);
        var replacement = Assert.Single(
            qrRepository.Identifiers,
            identifier => identifier.IsActive);
        Assert.Equal(2, replacement.TokenVersion);
        var rotationEvent = Assert.Single(qrRepository.Events);
        Assert.Equal(AssetEventType.QrRotated, rotationEvent.Type);
        Assert.DoesNotContain("new-token", rotationEvent.DetailsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("protected-new", rotationEvent.DetailsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SERIAL-PRIVATE", rotationEvent.DetailsJson, StringComparison.Ordinal);
    }

    private sealed class FakeQrRepository : IQrRepository
    {
        public FakeQrRepository(QrIdentifier current)
        {
            Identifiers.Add(current);
        }

        public List<QrIdentifier> Identifiers { get; } = [];

        public List<AssetEvent> Events { get; } = [];

        public int RotationSaveCount { get; private set; }

        public int NormalSaveCount { get; private set; }

        public Task<QrIdentifier?> GetActiveAsync(
            Guid assetId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Identifiers.SingleOrDefault(
                    identifier =>
                        identifier.AssetId == assetId &&
                        identifier.IsActive));

        public Task<QrIdentifier?> GetActiveByTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Identifiers.SingleOrDefault(
                    identifier =>
                        identifier.TokenHash == tokenHash &&
                        identifier.IsActive));

        public Task<int> GetLatestTokenVersionAsync(
            Guid assetId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Identifiers
                    .Where(identifier => identifier.AssetId == assetId)
                    .Select(identifier => identifier.TokenVersion)
                    .DefaultIfEmpty()
                    .Max());

        public void Add(QrIdentifier identifier) => Identifiers.Add(identifier);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            NormalSaveCount++;
            return Task.CompletedTask;
        }

        public Task SaveRotationAsync(
            QrIdentifier replacement,
            AssetEvent rotationEvent,
            CancellationToken cancellationToken)
        {
            Assert.DoesNotContain(
                Identifiers,
                identifier => identifier.IsActive);
            Identifiers.Add(replacement);
            Events.Add(rotationEvent);
            RotationSaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class QrInventoryRepository : IInventoryRepository
    {
        private readonly Asset _asset;
        private readonly AssetCategory _category;

        public QrInventoryRepository(Asset asset, AssetCategory category)
        {
            _asset = asset;
            _category = category;
        }

        public Task<Asset?> GetAssetAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(id == _asset.Id ? _asset : null);

        public Task<AssetView?> GetAssetViewAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                id == _asset.Id
                    ? new AssetView(
                        _asset.Id,
                        _asset.AssetTag,
                        _asset.DeviceName,
                        _category.Id,
                        _category.Code,
                        _category.Name,
                        _asset.Description,
                        _asset.Manufacturer,
                        _asset.Model,
                        _asset.SerialNumber,
                        _asset.LanMacAddress,
                        _asset.BiosGuid,
                        _asset.Ownership,
                        _asset.Status,
                        _asset.Condition,
                        null,
                        null,
                        null,
                        null,
                        new WarrantySummaryView(
                            Inventory.Domain.Warranties.WarrantyState.NotRecorded,
                            null,
                            null,
                            null),
                        _asset.OperatingSystem,
                        _asset.OperatingSystemVersion,
                        _asset.Windows11Compliant,
                        _asset.PurchaseDate,
                        _asset.PurchaseCost,
                        _asset.Currency,
                        _asset.Supplier,
                        _asset.Notes,
                        _asset.LegacyId,
                        _asset.CreatedAtUtc,
                        _asset.UpdatedAtUtc,
                        _asset.RetiredAtUtc,
                        _asset.RetirementReason,
                        _asset.DisposedAtUtc,
                        _asset.DisposalReason,
                        _asset.Version)
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
            Task.FromResult(id == _category.Id ? _category : null);

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
            throw new NotSupportedException();

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

        public void AddAsset(Asset asset) => throw new NotSupportedException();

        public void AddAssetEvent(AssetEvent assetEvent) =>
            throw new NotSupportedException();

        public void AddCategory(AssetCategory category) =>
            throw new NotSupportedException();

        public void AddLocation(Location location) =>
            throw new NotSupportedException();

        public void AddAuditLog(AuditLog auditLog) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTokenProtector : IQrTokenProtector
    {
        public QrTokenMaterial Create() =>
            new("new-token", "hash-new", "protected-new");

        public string Hash(string token) => $"hash-{token}";

        public string Unprotect(string protectedToken) => protectedToken;
    }

    private sealed class UnusedRenderer : IQrCodeRenderer
    {
        public byte[] Render(string payload, QrImageFormat format) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSchoolSettingsProvider : ISchoolSettingsProvider
    {
        public Task<SchoolSettingsSnapshot> GetAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new SchoolSettingsSnapshot(
                    "Test School",
                    "TEST",
                    "Europe/London",
                    "https://inventory.test/scan",
                    60));
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Now;

        public DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public bool IsAuthenticated => true;

        public IReadOnlyCollection<string> Roles => ["Administrator"];

        public IReadOnlyCollection<string> Permissions => [];

        public CurrentActor RequireActor() =>
            new(
                ActorId,
                "Test Administrator",
                "127.0.0.1",
                "test",
                "test-correlation");
    }
}
