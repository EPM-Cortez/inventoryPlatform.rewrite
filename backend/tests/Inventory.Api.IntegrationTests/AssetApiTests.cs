using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Contracts.V1;

namespace Inventory.Api.IntegrationTests;

public sealed class AssetApiTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public AssetApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AssetSearchRejectsAnOffsetThatWouldOverflow()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);

        var response = await client.GetAsync(
            "/api/v1/assets?page=2147483647&pageSize=200");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanCreateRetrieveAndAuditAnAsset()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];

        var categoryResponse = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = $"CAT-{suffix}",
                Name = "Integration category",
            },
            ApiTestClient.JsonOptions);
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(category);

        var createRequest = new CreateAssetRequest
        {
            AssetTag = $"SCH-{suffix}",
            DeviceName = $"STR-ICT-{suffix}",
            CategoryId = category.Id,
            Description = "ICT support laptop",
            Manufacturer = "Dell",
            Model = "Latitude",
            SerialNumber = $"SER-{suffix}",
            LanMacAddress = "74-86-e2-3a-00-96",
            BiosGuid = Guid.Parse("4C4C4544-0030-3310-8043-C3C04F475233"),
            Ownership = "Leased",
            Status = AssetStatusContract.InStock,
            Condition = AssetConditionContract.Good,
            PurchaseDate = new DateOnly(2026, 1, 1),
            PurchaseCost = 1000m,
            Currency = "GBP",
            OperatingSystem = "Windows",
            OperatingSystemVersion = "11",
            Windows11Compliant = true,
        };
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/assets",
            createRequest,
            ApiTestClient.JsonOptions);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        var created = await createResponse.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(createRequest.AssetTag, created.AssetTag);
        Assert.Equal(createRequest.DeviceName, created.DeviceName);
        Assert.Equal(createRequest.Description, created.Description);
        Assert.Equal("74:86:E2:3A:00:96", created.LanMacAddress);
        Assert.Equal(createRequest.BiosGuid, created.BiosGuid);
        Assert.Equal(createRequest.Ownership, created.Ownership);
        Assert.Equal(createRequest.OperatingSystem, created.OperatingSystem);
        Assert.Equal(createRequest.OperatingSystemVersion, created.OperatingSystemVersion);
        Assert.True(created.Windows11Compliant);

        var list = await client.GetFromJsonAsync<PagedResponse<AssetListItemResponse>>(
            $"/api/v1/assets?q={Uri.EscapeDataString(createRequest.DeviceName)}",
            ApiTestClient.JsonOptions);
        var listed = Assert.Single(list?.Items ?? []);
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal(created.DeviceName, listed.DeviceName);
        Assert.Equal(created.LanMacAddress, listed.LanMacAddress);

        var byId = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{created.Id}",
            ApiTestClient.JsonOptions);
        Assert.Equal(created.Id, byId?.Id);

        var byTag = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/by-tag/{createRequest.AssetTag}",
            ApiTestClient.JsonOptions);
        Assert.Equal(created.Id, byTag?.Id);

        var history = await client.GetFromJsonAsync<PagedResponse<AssetEventResponse>>(
            $"/api/v1/assets/{created.Id}/history",
            ApiTestClient.JsonOptions);
        var eventItem = Assert.Single(history?.Items ?? []);
        Assert.Equal("AssetCreated", eventItem.Type);
    }

    [Fact]
    public async Task DuplicateTagAndStaleUpdateReturn409ProblemDetails()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(client, suffix);

        var firstRequest = new CreateAssetRequest
        {
            AssetTag = $"DUP-{suffix}",
            CategoryId = category.Id,
            SerialNumber = $"SER-A-{suffix}",
        };
        var firstResponse = await client.PostAsJsonAsync(
            "/api/v1/assets",
            firstRequest,
            ApiTestClient.JsonOptions);
        firstResponse.EnsureSuccessStatusCode();
        var created = await firstResponse.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(created);

        var duplicateResponse = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = firstRequest.AssetTag.ToLowerInvariant(),
                CategoryId = category.Id,
                SerialNumber = $"SER-B-{suffix}",
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        var duplicateProblem = await duplicateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            "duplicate-asset-tag",
            duplicateProblem.GetProperty("type").GetString(),
            StringComparison.Ordinal);

        var staleUpdate = new UpdateAssetRequest
        {
            AssetTag = created.AssetTag,
            DeviceName = created.DeviceName,
            CategoryId = created.Category.Id,
            Description = created.Description,
            Manufacturer = created.Manufacturer,
            Model = "Changed",
            SerialNumber = created.SerialNumber,
            LanMacAddress = created.LanMacAddress,
            BiosGuid = created.BiosGuid,
            Ownership = created.Ownership,
            Status = created.Status,
            Condition = created.Condition,
            PurchaseDate = created.PurchaseDate,
            PurchaseCost = created.PurchaseCost,
            Currency = created.Currency,
            Supplier = created.Supplier,
            OperatingSystem = created.OperatingSystem,
            OperatingSystemVersion = created.OperatingSystemVersion,
            Windows11Compliant = created.Windows11Compliant,
            Notes = created.Notes,
            LegacyId = created.LegacyId,
            Version = Guid.NewGuid(),
        };
        var conflictResponse = await client.PutAsJsonAsync(
            $"/api/v1/assets/{created.Id}",
            staleUpdate,
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
    }

    [Fact]
    public async Task TerminalStatusesRequireDisposalWorkflowAndDisposedAssetCannotBeRestored()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(client, suffix);

        var invalidCreate = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = $"TERM-{suffix}",
                CategoryId = category.Id,
                Status = AssetStatusContract.Disposed,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);

        var create = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = $"DISP-{suffix}",
                CategoryId = category.Id,
                Status = AssetStatusContract.InStock,
            },
            ApiTestClient.JsonOptions);
        create.EnsureSuccessStatusCode();
        var asset = await create.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(asset);

        var dispose = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/dispose",
            new DisposeAssetRequest
            {
                Version = asset.Version,
                Reason = "Certified recycler collection",
            },
            ApiTestClient.JsonOptions);
        dispose.EnsureSuccessStatusCode();
        var disposed = await dispose.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(disposed);
        Assert.Equal(AssetStatusContract.Disposed, disposed.Status);
        Assert.Equal("Certified recycler collection", disposed.DisposalReason);
        Assert.NotNull(disposed.DisposedAtUtc);

        var history = await client.GetFromJsonAsync<PagedResponse<AssetEventResponse>>(
            $"/api/v1/assets/{asset.Id}/history",
            ApiTestClient.JsonOptions);
        Assert.Contains(history?.Items ?? [], item => item.Type == "Disposed");

        var restore = await client.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/restore",
            new VersionRequest(disposed.Version),
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, restore.StatusCode);
    }

    private static async Task<CategoryResponse> CreateCategoryAsync(
        HttpClient client,
        string suffix)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = $"CAT-{suffix}",
                Name = "Integration category",
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CategoryResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Category response was empty.");
    }
}
