using Inventory.Application.Abstractions;
using Inventory.Application.Assets;
using Inventory.Application.Common;
using Inventory.Application.Labels;
using Inventory.Application.Qr;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Inventory.Domain.Locations;

namespace Inventory.Application.Tests;

public sealed class LabelServiceTests
{
    [Fact]
    public async Task IssueMissingQrRequiresTheQrGeneratePermission()
    {
        var service = CreateService(permissions: ["qr.print"]);
        var command = new PrintLabelsCommand(
            LabelStock.BrotherDk11209,
            StartPosition: 1,
            [new LabelBatchItem(Guid.NewGuid(), Quantity: 1)],
            IssueMissingQr: true);

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(
            () => service.PrintAsync(command, "labels.pdf", CancellationToken.None));

        Assert.Equal("qr_generate_required", exception.Code);
    }

    [Fact]
    public async Task PrintingWithoutIssuingQrDoesNotRequireQrGenerate()
    {
        var service = CreateService(permissions: ["qr.print"]);
        var command = new PrintLabelsCommand(
            LabelStock.BrotherDk11209,
            StartPosition: 1,
            [new LabelBatchItem(Guid.NewGuid(), Quantity: 1)],
            IssueMissingQr: false);

        // The permission gate must not fire; the request then fails on the unknown
        // asset, proving the service progressed past the new check.
        await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => service.PrintAsync(command, "labels.pdf", CancellationToken.None));
    }

    private static LabelService CreateService(IReadOnlyCollection<string> permissions)
    {
        var currentUser = new FakeCurrentUserContext(permissions);
        var inventoryRepository = new EmptyInventoryRepository();
        var qrService = new QrService(
            inventoryRepository,
            new EmptyQrRepository(),
            new FakeTokenProtector(),
            new FakeQrRenderer(),
            new FakeSchoolSettingsProvider(),
            new FakeClock(),
            currentUser);
        return new LabelService(
            inventoryRepository,
            qrService,
            new FakeLabelRenderer(),
            new FakeClock(),
            currentUser);
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        private readonly IReadOnlyCollection<string> _permissions;

        public FakeCurrentUserContext(IReadOnlyCollection<string> permissions)
        {
            _permissions = permissions;
        }

        public bool IsAuthenticated => true;

        public IReadOnlyCollection<string> Roles => ["Technician"];

        public IReadOnlyCollection<string> Permissions => _permissions;

        public CurrentActor RequireActor() =>
            new(
                Guid.NewGuid(),
                "Test Technician",
                "127.0.0.1",
                "test",
                "test-correlation");
    }

    private sealed class EmptyInventoryRepository : IInventoryRepository
    {
        public Task<Asset?> GetAssetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Asset?>(null);

        public Task<AssetView?> GetAssetViewAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<AssetView?>(null);

        public Task<AssetView?> GetAssetViewByTagAsync(
            string normalizedAssetTag,
            CancellationToken cancellationToken) =>
            Task.FromResult<AssetView?>(null);

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

        public Task<AssetCategory?> GetCategoryAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> CategoryCodeExistsAsync(
            string normalizedCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AssetCategory>> ListCategoriesAsync(
            bool includeInactive,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Location?> GetLocationAsync(Guid id, CancellationToken cancellationToken) =>
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

        public void AddAssetEvent(AssetEvent assetEvent) => throw new NotSupportedException();

        public void AddCategory(AssetCategory category) => throw new NotSupportedException();

        public void AddLocation(Location location) => throw new NotSupportedException();

        public void AddAuditLog(AuditLog auditLog) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyQrRepository : IQrRepository
    {
        public Task<Inventory.Domain.Qr.QrIdentifier?> GetActiveAsync(
            Guid assetId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Inventory.Domain.Qr.QrIdentifier?>(null);

        public Task<Inventory.Domain.Qr.QrIdentifier?> GetActiveByTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken) =>
            Task.FromResult<Inventory.Domain.Qr.QrIdentifier?>(null);

        public Task<int> GetLatestTokenVersionAsync(
            Guid assetId,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public void Add(Inventory.Domain.Qr.QrIdentifier identifier) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveRotationAsync(
            Inventory.Domain.Qr.QrIdentifier replacement,
            AssetEvent rotationEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTokenProtector : IQrTokenProtector
    {
        public QrTokenMaterial Create() =>
            new("token-material-value-123", "hash", "protected");

        public string Hash(string token) => "hash";

        public string Unprotect(string protectedToken) => "token";
    }

    private sealed class FakeQrRenderer : IQrCodeRenderer
    {
        public byte[] Render(string payload, QrImageFormat format) => [];
    }

    private sealed class FakeSchoolSettingsProvider : ISchoolSettingsProvider
    {
        public Task<SchoolSettingsSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(
                new SchoolSettingsSnapshot(
                    "Test School",
                    "TEST",
                    "Europe/London",
                    "https://inventory.test/scan",
                    60));
    }

    private sealed class FakeLabelRenderer : IAssetLabelPdfRenderer
    {
        public LabelPdfDocument Render(
            LabelStock stock,
            int startPosition,
            IReadOnlyList<AssetLabelContent> labels,
            string fileName) =>
            new([], fileName, labels.Count, 1);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

        public DateOnly Today => new(2026, 7, 25);
    }
}
