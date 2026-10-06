using System.Net;
using System.Net.Http.Json;
using Inventory.Contracts.V1;

namespace Inventory.Api.IntegrationTests;

public sealed class DashboardStocktakeApiTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public DashboardStocktakeApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DashboardCountsActiveCategoryEstateAndProvidesPagedDrillDowns()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var anonymous = _factory.CreateHttpsClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(admin, suffix, $"Keyboards {suffix}");
        var library = await CreateLocationAsync(admin, $"LIB-{suffix}", "Library");
        var office = await CreateLocationAsync(admin, $"OFF-{suffix}", "Office");
        var first = await CreateAssetAsync(admin, category.Id, library.Id, $"KEY-1-{suffix}");
        _ = await CreateAssetAsync(admin, category.Id, office.Id, $"KEY-2-{suffix}");
        var disposed = await CreateAssetAsync(
            admin,
            category.Id,
            library.Id,
            $"KEY-OLD-{suffix}");
        var disposeResponse = await admin.PostAsJsonAsync(
            $"/api/v1/assets/{disposed.Id}/dispose",
            new DisposeAssetRequest
            {
                Version = disposed.Version,
                Reason = "Disposed before the stock count",
            },
            ApiTestClient.JsonOptions);
        disposeResponse.EnsureSuccessStatusCode();

        var unauthorized = await anonymous.GetAsync("/api/v1/dashboard/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var summary = await admin.GetFromJsonAsync<DashboardSummaryResponse>(
            "/api/v1/dashboard/summary?categoryLimit=200",
            ApiTestClient.JsonOptions);
        var categoryCount = Assert.Single(
            summary?.CategoryCounts ?? [],
            item => item.CategoryId == category.Id);
        Assert.Equal(2, categoryCount.Count);
        Assert.Contains(
            summary?.StatusCounts ?? [],
            item => item.Value == AssetStatusContract.Disposed && item.Count >= 1);

        var distribution = await admin.GetFromJsonAsync<InventoryDistributionResponse>(
            "/api/v1/dashboard/inventory-by-location?" +
            $"query={Uri.EscapeDataString(category.Name)}&page=1&pageSize=20",
            ApiTestClient.JsonOptions);
        Assert.Equal(2, distribution?.MatchingAssetCount);
        Assert.Equal(2, distribution?.Groups.TotalCount);
        Assert.Contains(
            distribution?.Groups.Items ?? [],
            item => item.LocationId == library.Id && item.Quantity == 1);
        Assert.Contains(
            distribution?.Groups.Items ?? [],
            item => item.LocationId == office.Id && item.Quantity == 1);

        var missingSerial = await admin.GetFromJsonAsync<
            PagedResponse<DashboardExceptionAssetResponse>>(
            "/api/v1/dashboard/exceptions/missingSerialNumber?page=1&pageSize=200",
            ApiTestClient.JsonOptions);
        Assert.Contains(missingSerial?.Items ?? [], item => item.AssetId == first.Id);
    }

    [Fact]
    public async Task StocktakeFreezesScopeAndReportsDiscrepanciesWithoutChangingAssets()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var auditor = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);
        using var anonymous = _factory.CreateHttpsClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(admin, suffix, $"Keyboards {suffix}");
        var library = await CreateLocationAsync(admin, $"LIB-{suffix}", "Library");
        var office = await CreateLocationAsync(admin, $"OFF-{suffix}", "Office");
        var expectedOne = await CreateAssetAsync(
            admin,
            category.Id,
            library.Id,
            $"STK-1-{suffix}");
        var expectedTwo = await CreateAssetAsync(
            admin,
            category.Id,
            library.Id,
            $"STK-2-{suffix}");
        var outsideScope = await CreateAssetAsync(
            admin,
            category.Id,
            office.Id,
            $"STK-3-{suffix}");

        var unauthorized = await anonymous.GetAsync("/api/v1/stocktakes");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var forbidden = await auditor.PostAsJsonAsync(
            "/api/v1/stocktakes",
            new StartStocktakeRequest
            {
                Name = "Forbidden",
                Scope = StocktakeScopeContract.All,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var startResponse = await admin.PostAsJsonAsync(
            "/api/v1/stocktakes",
            new StartStocktakeRequest
            {
                Name = $"Library stocktake {suffix}",
                Scope = StocktakeScopeContract.Location,
                LocationId = library.Id,
            },
            ApiTestClient.JsonOptions);
        Assert.True(
            startResponse.StatusCode == HttpStatusCode.Created,
            await startResponse.Content.ReadAsStringAsync());
        var started = await ReadAsync<StocktakeSessionResponse>(startResponse);
        Assert.Equal(2, started.Progress.Expected);
        Assert.Equal(2, started.Progress.Missing);
        var duplicateStart = await admin.PostAsJsonAsync(
            "/api/v1/stocktakes",
            new StartStocktakeRequest
            {
                Name = $"  library STOCKTAKE {suffix}  ",
                Scope = StocktakeScopeContract.Location,
                LocationId = library.Id,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, duplicateStart.StatusCode);

        var missingConditionResponse = await admin.PostAsJsonAsync(
            $"/api/v1/stocktakes/{started.Id}/observations",
            new
            {
                assetId = expectedOne.Id,
                observedLocationId = office.Id,
                version = started.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, missingConditionResponse.StatusCode);

        var firstRequest = new RecordStocktakeObservationRequest
        {
            AssetId = expectedOne.Id,
            ObservedLocationId = office.Id,
            ObservedCondition = AssetConditionContract.Damaged,
            Note = "Found in office",
            Version = started.Version,
        };
        var firstResponse = await admin.PostAsJsonAsync(
            $"/api/v1/stocktakes/{started.Id}/observations",
            firstRequest,
            ApiTestClient.JsonOptions);
        Assert.True(
            firstResponse.IsSuccessStatusCode,
            await firstResponse.Content.ReadAsStringAsync());
        var first = await ReadAsync<StocktakeObservationResponse>(firstResponse);
        Assert.True(first.Changed);

        var retryResponse = await admin.PostAsJsonAsync(
            $"/api/v1/stocktakes/{started.Id}/observations",
            firstRequest,
            ApiTestClient.JsonOptions);
        retryResponse.EnsureSuccessStatusCode();
        var retry = await ReadAsync<StocktakeObservationResponse>(retryResponse);
        Assert.False(retry.Changed);
        Assert.Equal(first.Session.Version, retry.Session.Version);

        var unexpectedResponse = await admin.PostAsJsonAsync(
            $"/api/v1/stocktakes/{started.Id}/observations",
            new RecordStocktakeObservationRequest
            {
                AssetTag = outsideScope.AssetTag.ToLowerInvariant(),
                ObservedLocationId = office.Id,
                ObservedCondition = AssetConditionContract.Good,
                Version = first.Session.Version,
            },
            ApiTestClient.JsonOptions);
        unexpectedResponse.EnsureSuccessStatusCode();
        var unexpected = await ReadAsync<StocktakeObservationResponse>(unexpectedResponse);
        Assert.Equal(2, unexpected.Session.Progress.Expected);
        Assert.Equal(1, unexpected.Session.Progress.Seen);
        Assert.Equal(1, unexpected.Session.Progress.Missing);
        Assert.Equal(1, unexpected.Session.Progress.Unexpected);
        Assert.Equal(1, unexpected.Session.Progress.WrongLocation);
        Assert.Equal(1, unexpected.Session.Progress.ConditionChanged);

        var missing = await admin.GetFromJsonAsync<PagedResponse<StocktakeItemResponse>>(
            $"/api/v1/stocktakes/{started.Id}/items?filter=missing&page=1&pageSize=20",
            ApiTestClient.JsonOptions);
        var missingItem = Assert.Single(missing?.Items ?? []);
        Assert.Equal(expectedTwo.Id, missingItem.AssetId);

        var completeResponse = await admin.PostAsJsonAsync(
            $"/api/v1/stocktakes/{started.Id}/complete",
            new StocktakeActionRequest(unexpected.Session.Version),
            ApiTestClient.JsonOptions);
        completeResponse.EnsureSuccessStatusCode();
        var completed = await ReadAsync<StocktakeSessionResponse>(completeResponse);
        Assert.Equal(StocktakeStatusContract.Completed, completed.Status);

        var unchangedAsset = await admin.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{expectedOne.Id}",
            ApiTestClient.JsonOptions);
        Assert.Equal(library.Id, unchangedAsset?.Location?.Id);
        Assert.Equal(AssetConditionContract.Good, unchangedAsset?.Condition);

        var afterClose = await admin.PostAsJsonAsync(
            $"/api/v1/stocktakes/{started.Id}/observations",
            new RecordStocktakeObservationRequest
            {
                AssetId = expectedTwo.Id,
                ObservedLocationId = library.Id,
                ObservedCondition = AssetConditionContract.Good,
                Version = completed.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, afterClose.StatusCode);
        Assert.Equal(
            2,
            await _factory.CountAuditActionsAsync("stocktake.asset.observed"));
    }

    private static async Task<CategoryResponse> CreateCategoryAsync(
        HttpClient client,
        string suffix,
        string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = $"KEY-{suffix}",
                Name = name,
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<CategoryResponse>(response);
    }

    private static async Task<LocationResponse> CreateLocationAsync(
        HttpClient client,
        string code,
        string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new CreateLocationRequest
            {
                Code = code,
                Name = name,
                Type = "Room",
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<LocationResponse>(response);
    }

    private static async Task<AssetResponse> CreateAssetAsync(
        HttpClient client,
        Guid categoryId,
        Guid locationId,
        string assetTag)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = assetTag,
                DeviceName = $"{assetTag}-device",
                CategoryId = categoryId,
                Status = AssetStatusContract.InStock,
                Condition = AssetConditionContract.Good,
                LocationId = locationId,
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<AssetResponse>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(ApiTestClient.JsonOptions)
        ?? throw new InvalidOperationException($"{typeof(T).Name} response was empty.");
}
