using System.Net;
using System.Net.Http.Json;
using Inventory.Contracts.V1;

namespace Inventory.Api.IntegrationTests;

public sealed class AssetOperationsApiTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public AssetOperationsApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ManagerCanConfigureAndRequestGeneratedAssetTags()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var category = await CreateCategoryAsync(client, suffix);
        var current = await client.GetFromJsonAsync<AssetTagPolicyResponse>(
            "/api/v1/settings/asset-tags",
            ApiTestClient.JsonOptions);
        Assert.NotNull(current);
        var prefix = $"GEN-{suffix}-";
        var updateResponse = await client.PutAsJsonAsync(
            "/api/v1/settings/asset-tags",
            new UpdateAssetTagPolicyRequest
            {
                Prefix = prefix,
                NumericWidth = 4,
                NextNumber = 1,
                Version = current.Version,
            },
            ApiTestClient.JsonOptions);
        updateResponse.EnsureSuccessStatusCode();

        var firstResponse = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                GenerateAssetTag = true,
                CategoryId = category.Id,
            },
            ApiTestClient.JsonOptions);
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions);
        Assert.Equal($"{prefix}0001", first?.AssetTag);

        var secondResponse = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                GenerateAssetTag = true,
                CategoryId = category.Id,
            },
            ApiTestClient.JsonOptions);
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions);
        Assert.Equal($"{prefix}0002", second?.AssetTag);

        var ambiguous = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = $"MANUAL-{suffix}",
                GenerateAssetTag = true,
                CategoryId = category.Id,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
    }

    [Fact]
    public async Task NumericEnumInJsonBodyIsRejectedBeforeTheHandlerRuns()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var request = JsonContent.Create(
            new
            {
                assets = new[]
                {
                    new
                    {
                        assetId = Guid.NewGuid(),
                        version = Guid.NewGuid(),
                    },
                },
                operation = new
                {
                    kind = "condition",
                    condition = 1,
                },
                reason = "Numeric enum regression test",
            },
            options: ApiTestClient.JsonOptions);

        using var response = await client.PostAsync(
            "/api/v1/assets/bulk-changes/preview",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkPreviewRejectsDriftWithoutPartiallyChangingAssets()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(client, suffix);
        var destination = await CreateLocationAsync(client, suffix);
        var first = await CreateAssetAsync(client, category.Id, $"BULK-A-{suffix}");
        var second = await CreateAssetAsync(client, category.Id, $"BULK-B-{suffix}");
        var preview = await PreviewLocationChangeAsync(
            client,
            destination.Id,
            first,
            second);
        var reusedPreview = await PreviewLocationChangeAsync(
            client,
            destination.Id,
            first,
            second);
        Assert.Equal(preview.JobId, reusedPreview.JobId);
        Assert.Equal(preview.Fingerprint, reusedPreview.Fingerprint);
        Assert.Equal(preview.Version, reusedPreview.Version);

        var driftResponse = await client.PutAsJsonAsync(
            $"/api/v1/assets/{second.Id}",
            ToUpdateRequest(second, model: "Changed after preview"),
            ApiTestClient.JsonOptions);
        driftResponse.EnsureSuccessStatusCode();
        var drifted = await driftResponse.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(drifted);

        var rejectedCommit = await client.PostAsJsonAsync(
            $"/api/v1/assets/bulk-changes/{preview.JobId}/commit",
            new CommitAssetBulkChangeRequest
            {
                Fingerprint = preview.Fingerprint,
                Version = preview.Version,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, rejectedCommit.StatusCode);

        var unchangedFirst = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{first.Id}",
            ApiTestClient.JsonOptions);
        var unchangedSecond = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{second.Id}",
            ApiTestClient.JsonOptions);
        Assert.Null(unchangedFirst?.Location);
        Assert.Null(unchangedSecond?.Location);

        var freshPreview = await PreviewLocationChangeAsync(
            client,
            destination.Id,
            unchangedFirst ?? throw new InvalidOperationException(),
            unchangedSecond ?? throw new InvalidOperationException());
        var commitResponse = await client.PostAsJsonAsync(
            $"/api/v1/assets/bulk-changes/{freshPreview.JobId}/commit",
            new CommitAssetBulkChangeRequest
            {
                Fingerprint = freshPreview.Fingerprint,
                Version = freshPreview.Version,
            },
            ApiTestClient.JsonOptions);
        commitResponse.EnsureSuccessStatusCode();
        var commit = await commitResponse.Content
            .ReadFromJsonAsync<AssetBulkChangeCommitResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(commit);
        Assert.Equal(AssetBulkChangeJobStatusContract.Committed, commit.Status);
        Assert.Equal(2, commit.AssetCount);

        var movedFirst = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{first.Id}",
            ApiTestClient.JsonOptions);
        var movedSecond = await client.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{second.Id}",
            ApiTestClient.JsonOptions);
        Assert.Equal(destination.Id, movedFirst?.Location?.Id);
        Assert.Equal(destination.Id, movedSecond?.Location?.Id);
    }

    [Fact]
    public async Task ActiveBundleUniquenessReleasesOnArchiveAndRechecksOnRestore()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var category = await CreateCategoryAsync(client, suffix);
        var laptop = await CreateAssetAsync(
            client,
            category.Id,
            $"KIT-L-{suffix}");
        var charger = await CreateAssetAsync(
            client,
            category.Id,
            $"KIT-C-{suffix}");

        var firstResponse = await client.PostAsJsonAsync(
            "/api/v1/asset-bundles",
            new CreateAssetBundleRequest
            {
                Code = $"KIT-A-{suffix}",
                Name = $"Primary kit {suffix}",
                Members =
                [
                    new AssetBundleMemberRequest
                    {
                        AssetId = laptop.Id,
                        Role = AssetBundleMemberRoleContract.Laptop,
                    },
                    new AssetBundleMemberRequest
                    {
                        AssetId = charger.Id,
                        Role = AssetBundleMemberRoleContract.Charger,
                    },
                ],
            },
            ApiTestClient.JsonOptions);
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<AssetBundleResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(first);
        Assert.Equal(2, first.Members.Count);

        var conflictingCreate = await client.PostAsJsonAsync(
            "/api/v1/asset-bundles",
            BundleRequest($"KIT-B-{suffix}", $"Second kit {suffix}", laptop.Id),
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, conflictingCreate.StatusCode);

        var archiveResponse = await client.PostAsJsonAsync(
            $"/api/v1/asset-bundles/{first.Id}/archive",
            new VersionRequest(first.Version),
            ApiTestClient.JsonOptions);
        archiveResponse.EnsureSuccessStatusCode();
        var archived = await archiveResponse.Content
            .ReadFromJsonAsync<AssetBundleResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(archived);
        Assert.True(archived.IsArchived);
        Assert.Equal(2, archived.Members.Count);

        var secondResponse = await client.PostAsJsonAsync(
            "/api/v1/asset-bundles",
            BundleRequest($"KIT-B-{suffix}", $"Second kit {suffix}", laptop.Id),
            ApiTestClient.JsonOptions);
        secondResponse.EnsureSuccessStatusCode();

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/v1/asset-bundles/{first.Id}/restore",
            new VersionRequest(archived.Version),
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, restoreResponse.StatusCode);
    }

    private static CreateAssetBundleRequest BundleRequest(
        string code,
        string name,
        Guid assetId) =>
        new()
        {
            Code = code,
            Name = name,
            Members =
            [
                new AssetBundleMemberRequest
                {
                    AssetId = assetId,
                    Role = AssetBundleMemberRoleContract.Other,
                    OtherRole = "Shared peripheral",
                },
            ],
        };

    private static async Task<AssetBulkChangePreviewResponse>
        PreviewLocationChangeAsync(
            HttpClient client,
            Guid locationId,
            params AssetResponse[] assets)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/assets/bulk-changes/preview",
            new PreviewAssetBulkChangeRequest
            {
                Assets = assets
                    .Select(
                        asset => new AssetBulkChangeSelectionRequest(
                            asset.Id,
                            asset.Version))
                    .ToArray(),
                Operation = new AssetBulkChangeOperationRequest
                {
                    Kind = AssetBulkChangeOperationContract.SetLocation,
                    LocationId = locationId,
                },
                Reason = "Move selected equipment to the ICT store",
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<AssetBulkChangePreviewResponse>(
                ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Bulk preview response was empty.");
    }

    private static async Task<CategoryResponse> CreateCategoryAsync(
        HttpClient client,
        string suffix)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = $"OPS-{suffix}",
                Name = $"Asset operations {suffix}",
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CategoryResponse>(
                   ApiTestClient.JsonOptions)
               ?? throw new InvalidOperationException("Category response was empty.");
    }

    private static async Task<LocationResponse> CreateLocationAsync(
        HttpClient client,
        string suffix)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new CreateLocationRequest
            {
                Code = $"STORE-{suffix}",
                Name = $"ICT Store {suffix}",
                Type = "Store",
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LocationResponse>(
                   ApiTestClient.JsonOptions)
               ?? throw new InvalidOperationException("Location response was empty.");
    }

    private static async Task<AssetResponse> CreateAssetAsync(
        HttpClient client,
        Guid categoryId,
        string assetTag)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = assetTag,
                CategoryId = categoryId,
                Status = AssetStatusContract.InStock,
                Condition = AssetConditionContract.Good,
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssetResponse>(
                   ApiTestClient.JsonOptions)
               ?? throw new InvalidOperationException("Asset response was empty.");
    }

    private static UpdateAssetRequest ToUpdateRequest(
        AssetResponse asset,
        string? model) =>
        new()
        {
            AssetTag = asset.AssetTag,
            DeviceName = asset.DeviceName,
            CategoryId = asset.Category.Id,
            Description = asset.Description,
            Manufacturer = asset.Manufacturer,
            Model = model,
            SerialNumber = asset.SerialNumber,
            LanMacAddress = asset.LanMacAddress,
            BiosGuid = asset.BiosGuid,
            Ownership = asset.Ownership,
            Status = asset.Status,
            Condition = asset.Condition,
            LocationId = asset.Location?.Id,
            PurchaseDate = asset.PurchaseDate,
            PurchaseCost = asset.PurchaseCost,
            Currency = asset.Currency,
            Supplier = asset.Supplier,
            OperatingSystem = asset.OperatingSystem,
            OperatingSystemVersion = asset.OperatingSystemVersion,
            Windows11Compliant = asset.Windows11Compliant,
            Notes = asset.Notes,
            LegacyId = asset.LegacyId,
            Version = asset.Version,
        };
}
