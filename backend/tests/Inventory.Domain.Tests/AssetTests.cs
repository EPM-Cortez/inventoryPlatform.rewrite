using Inventory.Domain.Assets;
using Inventory.Domain.Common;

namespace Inventory.Domain.Tests;

public sealed class AssetTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateNormalizesIdentifiersAndSetsInitialVersion()
    {
        var asset = Asset.Create(
            ValidDetails() with
            {
                AssetTag = "  sch-001  ",
                SerialNumber = " abc-123 ",
                LanMacAddress = "74-86-e2-3a-00-96",
                Currency = "gbp",
            },
            Now);

        Assert.Equal("sch-001", asset.AssetTag);
        Assert.Equal("SCH-001", asset.AssetTagNormalized);
        Assert.Equal("abc-123", asset.SerialNumber);
        Assert.Equal("ABC-123", asset.SerialNumberNormalized);
        Assert.Equal("74:86:E2:3A:00:96", asset.LanMacAddress);
        Assert.Equal("GBP", asset.Currency);
        Assert.NotEqual(Guid.Empty, asset.Version);
        Assert.Equal(Now, asset.CreatedAtUtc);
        Assert.Equal(Now, asset.UpdatedAtUtc);
    }

    [Fact]
    public void CreateRejectsAssignedStatusWithoutAssignmentWorkflow()
    {
        var exception = Assert.Throws<DomainRuleException>(
            () => Asset.Create(
                ValidDetails() with { Status = AssetStatus.Assigned },
                Now));

        Assert.Equal("assignment_required", exception.Code);
    }

    [Theory]
    [InlineData(AssetStatus.Retired)]
    [InlineData(AssetStatus.Disposed)]
    public void CreateRejectsTerminalStatusesWithoutLifecycleWorkflow(AssetStatus status)
    {
        var exception = Assert.Throws<DomainRuleException>(
            () => Asset.Create(
                ValidDetails() with { Status = status },
                Now));

        Assert.Equal("lifecycle_action_required", exception.Code);
    }

    [Fact]
    public void CreateRequiresCurrencyWhenPurchaseCostIsPresent()
    {
        var exception = Assert.Throws<DomainRuleException>(
            () => Asset.Create(
                ValidDetails() with { PurchaseCost = 100m, Currency = null },
                Now));

        Assert.Equal("currency_required", exception.Code);
    }

    [Fact]
    public void CreateRejectsPurchaseCostWithFractionalMinorUnits()
    {
        var exception = Assert.Throws<DomainRuleException>(
            () => Asset.Create(
                ValidDetails() with { PurchaseCost = 100.001m },
                Now));

        Assert.Equal("invalid_purchase_cost", exception.Code);
    }

    [Fact]
    public void CreateRejectsInvalidLanMacAddress()
    {
        var exception = Assert.Throws<DomainRuleException>(
            () => Asset.Create(
                ValidDetails() with { LanMacAddress = "not-a-mac" },
                Now));

        Assert.Equal("invalid_lan_mac_address", exception.Code);
    }

    [Fact]
    public void UpdateRejectsAStaleVersion()
    {
        var asset = Asset.Create(ValidDetails(), Now);

        var exception = Assert.Throws<DomainRuleException>(
            () => asset.Update(
                ValidDetails() with { Model = "Changed" },
                Guid.NewGuid(),
                Now.AddMinutes(1)));

        Assert.Equal("concurrency_conflict", exception.Code);
    }

    [Fact]
    public void NoOpUpdateDoesNotRotateVersionOrTimestamp()
    {
        var asset = Asset.Create(ValidDetails(), Now);
        var version = asset.Version;

        var changes = asset.Update(ValidDetails(), version, Now.AddMinutes(1));

        Assert.Empty(changes);
        Assert.Equal(version, asset.Version);
        Assert.Equal(Now, asset.UpdatedAtUtc);
    }

    [Fact]
    public void RetireAndRestorePreserveLifecycleRules()
    {
        var asset = Asset.Create(ValidDetails(), Now);

        asset.Retire("End of useful life", asset.Version, Now.AddDays(1));
        var retiredVersion = asset.Version;

        Assert.Equal(AssetStatus.Retired, asset.Status);
        Assert.Equal("End of useful life", asset.RetirementReason);
        Assert.Equal(Now.AddDays(1), asset.RetiredAtUtc);

        asset.Restore(retiredVersion, Now.AddDays(2));

        Assert.Equal(AssetStatus.InStock, asset.Status);
        Assert.Null(asset.RetirementReason);
        Assert.Null(asset.RetiredAtUtc);
        Assert.NotEqual(retiredVersion, asset.Version);
    }

    [Fact]
    public void DisposeIsTerminalAndRecordsSeparateLifecycleMetadata()
    {
        var asset = Asset.Create(ValidDetails(), Now);
        asset.Retire("Withdrawn from service", asset.Version, Now.AddDays(1));
        var retiredAt = asset.RetiredAtUtc;
        var retirementReason = asset.RetirementReason;

        asset.Dispose("Transferred to certified recycler", asset.Version, Now.AddDays(2));

        Assert.Equal(AssetStatus.Disposed, asset.Status);
        Assert.Equal(Now.AddDays(2), asset.DisposedAtUtc);
        Assert.Equal("Transferred to certified recycler", asset.DisposalReason);
        Assert.Equal(retiredAt, asset.RetiredAtUtc);
        Assert.Equal(retirementReason, asset.RetirementReason);

        var restore = Assert.Throws<DomainRuleException>(
            () => asset.Restore(asset.Version, Now.AddDays(3)));
        Assert.Equal("not_retired", restore.Code);

        var update = Assert.Throws<DomainRuleException>(
            () => asset.Update(
                ValidDetails(),
                asset.Version,
                Now.AddDays(3)));
        Assert.Equal("asset_not_mutable", update.Code);
    }

    [Fact]
    public void AssignTransferAndReturnControlStatusAndVersion()
    {
        var asset = Asset.Create(ValidDetails(), Now);

        asset.Assign(asset.Version, Now.AddMinutes(1));
        var assignedVersion = asset.Version;
        Assert.Equal(AssetStatus.Assigned, asset.Status);

        asset.Transfer(assignedVersion, Now.AddMinutes(2));
        var transferredVersion = asset.Version;
        Assert.Equal(AssetStatus.Assigned, asset.Status);
        Assert.NotEqual(assignedVersion, transferredVersion);

        var returnLocation = Guid.NewGuid();
        asset.Return(
            transferredVersion,
            Now.AddMinutes(3),
            AssetCondition.Damaged,
            returnLocation);

        Assert.Equal(AssetStatus.InStock, asset.Status);
        Assert.Equal(AssetCondition.Damaged, asset.Condition);
        Assert.Equal(returnLocation, asset.LocationId);
        Assert.NotEqual(transferredVersion, asset.Version);
    }

    [Theory]
    [InlineData(AssetStatus.Repair)]
    [InlineData(AssetStatus.Lost)]
    [InlineData(AssetStatus.Stolen)]
    public void AssignmentRejectsUnavailableStatuses(AssetStatus status)
    {
        var asset = Asset.Create(ValidDetails() with { Status = status }, Now);

        var exception = Assert.Throws<DomainRuleException>(
            () => asset.Assign(asset.Version, Now.AddMinutes(1)));

        Assert.Equal("assignment_not_allowed", exception.Code);
    }

    private static AssetDetails ValidDetails() =>
        new(
            "SCH-001",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Dell",
            "Latitude",
            "SERIAL-001",
            AssetStatus.InStock,
            AssetCondition.Good,
            null,
            new DateOnly(2026, 1, 1),
            1000m,
            "GBP",
            "Supplier",
            "Notes",
            "LEGACY-1");
}
