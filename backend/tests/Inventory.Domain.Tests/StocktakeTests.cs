using Inventory.Domain.Assets;
using Inventory.Domain.Common;
using Inventory.Domain.Stocktakes;

namespace Inventory.Domain.Tests;

public sealed class StocktakeTests
{
    private static readonly Guid ActorId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LocationId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset StartedAt =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ScopeRequiresExactlyItsOwnReference()
    {
        var allWithLocation = Assert.Throws<DomainRuleException>(
            () => StocktakeSession.Start(
                "All",
                StocktakeScope.All,
                LocationId,
                null,
                StartedAt,
                ActorId,
                "Technician"));
        var locationWithoutId = Assert.Throws<DomainRuleException>(
            () => StocktakeSession.Start(
                "Room",
                StocktakeScope.Location,
                null,
                null,
                StartedAt,
                ActorId,
                "Technician"));

        Assert.Equal("invalid_stocktake_scope", allWithLocation.Code);
        Assert.Equal("invalid_stocktake_scope", locationWithoutId.Code);
    }

    [Fact]
    public void UnchangedRescanIsIdempotentEvenWithOriginalVersion()
    {
        var session = CreateSession();
        var originalVersion = session.Version;

        Assert.True(
            session.RegisterObservation(
                originalVersion,
                StartedAt.AddMinutes(1),
                observationChanged: true));
        var changedVersion = session.Version;

        Assert.False(
            session.RegisterObservation(
                originalVersion,
                StartedAt.AddMinutes(2),
                observationChanged: false));
        Assert.Equal(changedVersion, session.Version);
    }

    [Fact]
    public void CompletedSessionRejectsFurtherScans()
    {
        var session = CreateSession();
        session.Complete(
            session.Version,
            StartedAt.AddHours(1),
            ActorId,
            "Technician");

        var exception = Assert.Throws<DomainRuleException>(
            () => session.RegisterObservation(
                session.Version,
                StartedAt.AddHours(2),
                observationChanged: false));

        Assert.Equal("stocktake_not_in_progress", exception.Code);
    }

    [Fact]
    public void ExpectedAssetRetainsCapturedRegisterValues()
    {
        var session = CreateSession();
        var expected = StocktakeExpectedAsset.Capture(
            session.Id,
            Guid.NewGuid(),
            "AST-100",
            "Library keyboard",
            Guid.NewGuid(),
            "KEY",
            "Keyboard",
            AssetStatus.InStock,
            AssetCondition.Good,
            LocationId,
            "LIB",
            "Library");

        Assert.Equal("AST-100", expected.AssetTag);
        Assert.Equal("Keyboard", expected.CategoryName);
        Assert.Equal("Library", expected.ExpectedLocationName);
        Assert.Equal(AssetCondition.Good, expected.ExpectedCondition);
    }

    private static StocktakeSession CreateSession() =>
        StocktakeSession.Start(
            "Summer stocktake",
            StocktakeScope.All,
            null,
            null,
            StartedAt,
            ActorId,
            "Technician");
}
