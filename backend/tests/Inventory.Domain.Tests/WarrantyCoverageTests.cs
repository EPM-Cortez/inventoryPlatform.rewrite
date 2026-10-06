using Inventory.Domain.Common;
using Inventory.Domain.Warranties;

namespace Inventory.Domain.Tests;

public sealed class WarrantyCoverageTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 7, 24);

    [Fact]
    public void CalculatesEveryWarrantyStateAtTheDateBoundaries()
    {
        Assert.Equal(
            WarrantyState.NotRecorded,
            Create(startDate: null, endDate: null).CalculateState(Today, 60));
        Assert.Equal(
            WarrantyState.Future,
            Create(Today.AddDays(1), Today.AddDays(90)).CalculateState(Today, 60));
        Assert.Equal(
            WarrantyState.Active,
            Create(Today.AddDays(-1), Today.AddDays(61)).CalculateState(Today, 60));
        Assert.Equal(
            WarrantyState.ExpiringSoon,
            Create(Today.AddDays(-1), Today.AddDays(60)).CalculateState(Today, 60));
        Assert.Equal(
            WarrantyState.ExpiringSoon,
            Create(Today.AddDays(-1), Today).CalculateState(Today, 60));
        Assert.Equal(
            WarrantyState.Expired,
            Create(Today.AddDays(-90), Today.AddDays(-1)).CalculateState(Today, 60));
    }

    [Fact]
    public void RejectsAnEndDateBeforeTheStartDate()
    {
        var exception = Assert.Throws<DomainRuleException>(
            () => Create(Today, Today.AddDays(-1)));

        Assert.Equal("invalid_warranty_dates", exception.Code);
    }

    [Fact]
    public void ArchiveRequiresCurrentVersionAndClearsPrimary()
    {
        var warranty = Create(Today, Today.AddDays(100), isPrimary: true);
        var stale = Assert.Throws<DomainRuleException>(
            () => warranty.Archive(Guid.NewGuid(), Now.AddHours(1)));
        Assert.Equal("concurrency_conflict", stale.Code);

        warranty.Archive(warranty.Version, Now.AddHours(1));

        Assert.True(warranty.IsArchived);
        Assert.False(warranty.IsPrimary);
        Assert.Equal(Now.AddHours(1), warranty.ArchivedAtUtc);
    }

    private static WarrantyCoverage Create(
        DateOnly? startDate,
        DateOnly? endDate,
        bool isPrimary = true) =>
        WarrantyCoverage.Create(
            Guid.NewGuid(),
            "Example Warranty Ltd",
            "Manufacturer",
            startDate,
            endDate,
            "REF-1",
            "Coverage notes",
            isPrimary,
            Now);
}
