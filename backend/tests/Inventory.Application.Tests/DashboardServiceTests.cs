using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Application.Dashboard;

namespace Inventory.Application.Tests;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task ExceptionQueueUsesTheSchoolBusinessDate()
    {
        var repository = new RecordingDashboardRepository();
        var service = new DashboardService(
            repository,
            new LondonSchoolSettings(),
            new FixedClock(new DateTimeOffset(2026, 7, 26, 23, 30, 0, TimeSpan.Zero)));

        _ = await service.GetExceptionQueueAsync(
            DashboardExceptionCodes.ExpiredWarranty,
            page: 1,
            pageSize: 50,
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 7, 27), repository.Today);
    }

    [Fact]
    public async Task InventoryDistributionNormalizesSearchAndPaging()
    {
        var repository = new RecordingDashboardRepository();
        var service = new DashboardService(
            repository,
            new LondonSchoolSettings(),
            new FixedClock(DateTimeOffset.UtcNow));

        _ = await service.GetInventoryDistributionAsync(
            new InventoryDistributionSearch(
                null,
                "  Keyboard  ",
                false,
                2,
                100),
            CancellationToken.None);

        Assert.Equal("Keyboard", repository.DistributionSearch?.Query);
        Assert.Equal(2, repository.DistributionSearch?.Page);
        Assert.Equal(100, repository.DistributionSearch?.PageSize);
    }

    [Fact]
    public async Task SummaryRejectsAnUnboundedCategoryList()
    {
        var service = new DashboardService(
            new RecordingDashboardRepository(),
            new LondonSchoolSettings(),
            new FixedClock(DateTimeOffset.UtcNow));

        var exception = await Assert.ThrowsAsync<RequestValidationException>(
            () => service.GetSummaryAsync(201, CancellationToken.None));

        Assert.Equal("invalid_category_limit", exception.Code);
    }

    private sealed class RecordingDashboardRepository : IDashboardRepository
    {
        public DateOnly Today { get; private set; }

        public InventoryDistributionSearch? DistributionSearch { get; private set; }

        public Task<DashboardSummaryView> GetSummaryAsync(
            int categoryLimit,
            DateOnly today,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InventoryDistributionView> GetInventoryDistributionAsync(
            InventoryDistributionSearch search,
            CancellationToken cancellationToken)
        {
            DistributionSearch = search;
            return Task.FromResult(
                new InventoryDistributionView(
                    new PageResult<InventoryDistributionItem>(
                        [],
                        search.Page,
                        search.PageSize,
                        0),
                    0));
        }

        public Task<PageResult<DashboardExceptionAssetView>> GetExceptionQueueAsync(
            string exceptionCode,
            DateOnly today,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            Today = today;
            return Task.FromResult(
                new PageResult<DashboardExceptionAssetView>([], page, pageSize, 0));
        }
    }

    private sealed class LondonSchoolSettings : ISchoolSettingsProvider
    {
        public Task<SchoolSettingsSnapshot> GetAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new SchoolSettingsSnapshot(
                    "School",
                    "SCH",
                    "Europe/London",
                    "https://inventory.example.test/scan",
                    60));
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }

        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
