using System.Globalization;
using System.Reflection;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Application.Warranties;
using Inventory.Domain.Warranties;

namespace Inventory.Application.Tests;

public sealed class WarrantyServiceTests
{
    [Theory]
    [InlineData(
        "2026-07-25T22:59:59+00:00",
        "2026-07-25",
        "2026-09-23",
        WarrantyState.ExpiringSoon,
        0)]
    [InlineData(
        "2026-07-25T23:00:00+00:00",
        "2026-07-26",
        "2026-09-24",
        WarrantyState.Expired,
        -1)]
    public async Task ExpiringWarrantyDatesRollAtSchoolMidnightDuringBst(
        string utcTimestamp,
        string expectedToday,
        string expectedWindowEnd,
        WarrantyState expectedState,
        int expectedDaysRemaining)
    {
        var clock = new FakeClock(
            DateTimeOffset.Parse(
                utcTimestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));
        var warranty = WarrantyCoverage.Create(
            Guid.NewGuid(),
            "Manufacturer",
            "Standard",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 25),
            null,
            null,
            isPrimary: true,
            new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));
        var warrantyRepository = new RecordingWarrantyRepository(warranty);
        var service = new WarrantyService(
            DispatchProxy.Create<IInventoryRepository, ThrowingProxy>(),
            warrantyRepository,
            new LondonSchoolSettingsProvider(),
            clock,
            new UnusedCurrentUserContext());

        var result = await service.ListExpiringAsync(
            withinDays: 60,
            page: 1,
            pageSize: 50,
            CancellationToken.None);

        Assert.Equal(
            DateOnly.Parse(expectedToday, CultureInfo.InvariantCulture),
            warrantyRepository.StartsOnOrBefore);
        Assert.Equal(
            DateOnly.Parse(expectedWindowEnd, CultureInfo.InvariantCulture),
            warrantyRepository.EndsOnOrBefore);
        var item = Assert.Single(result.Items);
        Assert.Equal(expectedState, item.State);
        Assert.Equal(expectedDaysRemaining, item.DaysRemaining);
    }

    private sealed class RecordingWarrantyRepository : IWarrantyRepository
    {
        private readonly WarrantyCoverage _warranty;

        public RecordingWarrantyRepository(WarrantyCoverage warranty)
        {
            _warranty = warranty;
        }

        public DateOnly StartsOnOrBefore { get; private set; }

        public DateOnly EndsOnOrBefore { get; private set; }

        public Task<PageResult<WarrantyCoverage>> ListExpiringAsync(
            DateOnly startsOnOrBefore,
            DateOnly endsOnOrBefore,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            StartsOnOrBefore = startsOnOrBefore;
            EndsOnOrBefore = endsOnOrBefore;
            return Task.FromResult(
                new PageResult<WarrantyCoverage>(
                    [_warranty],
                    page,
                    pageSize,
                    TotalCount: 1));
        }

        public Task<WarrantyCoverage?> GetAsync(
            Guid warrantyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WarrantyCoverage?> GetPrimaryAsync(
            Guid assetId,
            Guid? excludingWarrantyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WarrantyCoverage>> ListForAssetAsync(
            Guid assetId,
            bool includeArchived,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(WarrantyCoverage warranty) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class LondonSchoolSettingsProvider : ISchoolSettingsProvider
    {
        public Task<SchoolSettingsSnapshot> GetAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new SchoolSettingsSnapshot(
                    "School",
                    "SCH",
                    "Europe/London",
                    "https://inventory.example.school/scan",
                    60));
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }

        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    private sealed class UnusedCurrentUserContext : ICurrentUserContext
    {
        public bool IsAuthenticated => false;

        public IReadOnlyCollection<string> Roles => [];

        public IReadOnlyCollection<string> Permissions => [];

        public CurrentActor RequireActor() => throw new NotSupportedException();
    }

    public class ThrowingProxy : DispatchProxy
    {
        public ThrowingProxy()
        {
        }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            throw new NotSupportedException(
                $"{targetMethod?.Name ?? "Unknown method"} was not expected.");
    }
}
