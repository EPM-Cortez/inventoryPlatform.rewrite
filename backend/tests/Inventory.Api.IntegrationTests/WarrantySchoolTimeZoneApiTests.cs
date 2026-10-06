using System.Net.Http.Json;
using Inventory.Contracts.V1;
using Inventory.Domain.Assets;
using Inventory.Domain.Warranties;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class WarrantySchoolTimeZoneApiTests
{
    [Theory]
    [InlineData(
        "2026-07-25T22:59:59+00:00",
        WarrantyStateContract.ExpiringSoon,
        "expiringSoon",
        0)]
    [InlineData(
        "2026-07-25T23:00:00+00:00",
        WarrantyStateContract.Expired,
        "expired",
        -1)]
    public async Task AllWarrantyProjectionsRollAtLondonMidnightDuringBst(
        string utcTimestamp,
        WarrantyStateContract expectedState,
        string expectedFilter,
        int expectedDaysRemaining)
    {
        var utcNow = DateTimeOffset.Parse(
            utcTimestamp,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            utcNow: utcNow);
        _ = await factory.GetDatabaseStateAsync();

        var assetId = await SeedWarrantyFixtureAsync(factory, utcNow);
        using var administrator = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);

        var detail = await administrator.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{assetId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal(expectedState, detail.WarrantySummary.State);
        Assert.Equal(expectedDaysRemaining, detail.WarrantySummary.DaysRemaining);

        var matching = await administrator.GetFromJsonAsync<
            PagedResponse<AssetListItemResponse>>(
            $"/api/v1/assets?assetTag=BST-WARRANTY-001&warrantyState={expectedFilter}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(matching);
        var listed = Assert.Single(matching.Items);
        Assert.Equal(expectedState, listed.WarrantySummary.State);
        Assert.Equal(expectedDaysRemaining, listed.WarrantySummary.DaysRemaining);

        using var issueQr = await administrator.PostAsync(
            $"/api/v1/assets/{assetId}/qr",
            content: null);
        issueQr.EnsureSuccessStatusCode();
        var identifier = await issueQr.Content.ReadFromJsonAsync<QrIdentifierResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(identifier);
        var token = Uri.UnescapeDataString(
            new Uri(identifier.PayloadUrl).Segments[^1].Trim('/'));
        var resolution = await administrator.GetFromJsonAsync<QrResolutionResponse>(
            $"/api/v1/qr/{Uri.EscapeDataString(token)}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(resolution);
        Assert.Equal(expectedState, resolution.WarrantySummary.State);
        Assert.Equal(
            expectedDaysRemaining,
            resolution.WarrantySummary.DaysRemaining);

        using var export = await administrator.GetAsync(
            "/api/v1/exports/assets.csv" +
            $"?assetTag=BST-WARRANTY-001&warrantyState={expectedFilter}");
        export.EnsureSuccessStatusCode();
        Assert.Equal(
            "1",
            Assert.Single(export.Headers.GetValues("X-Inventory-Row-Count")));
        Assert.Contains(
            "BST-WARRANTY-001",
            await export.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    private static async Task<Guid> SeedWarrantyFixtureAsync(
        InventoryApiFactory factory,
        DateTimeOffset utcNow)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var category = AssetCategory.Create("BST-WARRANTY", "BST warranty");
        var asset = Asset.Create(
            new AssetDetails(
                AssetTag: "BST-WARRANTY-001",
                CategoryId: category.Id,
                Manufacturer: "Contoso",
                Model: "Boundary",
                SerialNumber: "BST-WARRANTY-SERIAL-001",
                Status: AssetStatus.InStock,
                Condition: AssetCondition.Good,
                LocationId: null,
                PurchaseDate: new DateOnly(2026, 1, 1),
                PurchaseCost: 1000m,
                Currency: "GBP",
                Supplier: "Supplier",
                Notes: "School timezone boundary fixture.",
                LegacyId: null,
                DeviceName: "BST-WARRANTY-DEVICE"),
            utcNow);
        var warranty = WarrantyCoverage.Create(
            asset.Id,
            "Manufacturer",
            "Standard",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 25),
            null,
            null,
            isPrimary: true,
            utcNow);
        dbContext.AssetCategories.Add(category);
        dbContext.Assets.Add(asset);
        dbContext.WarrantyCoverages.Add(warranty);
        await dbContext.SaveChangesAsync();
        return asset.Id;
    }
}
