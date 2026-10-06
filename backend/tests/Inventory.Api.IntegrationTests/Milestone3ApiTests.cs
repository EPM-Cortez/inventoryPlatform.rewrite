using System.Net;
using System.Net.Http.Json;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class Milestone3ApiTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public Milestone3ApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task QrIssueRenderResolveAndRotatePreservePrivacyAndRevokeTheOldToken()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var auditor = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);
        using var operatorClient = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        using var anonymous = _factory.CreateHttpsClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var asset = await CreateAssetAsync(admin, suffix);

        var missingImage = await admin.GetAsync($"/api/v1/assets/{asset.Id}/qr");
        Assert.Equal(HttpStatusCode.NotFound, missingImage.StatusCode);

        var issueResponse = await admin.PostAsync(
            $"/api/v1/assets/{asset.Id}/qr",
            content: null);
        Assert.Equal(HttpStatusCode.Created, issueResponse.StatusCode);
        var issued = await issueResponse.Content.ReadFromJsonAsync<QrIdentifierResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(issued);
        Assert.Equal(1, issued.TokenVersion);

        var issuedUri = new Uri(issued.PayloadUrl);
        var oldToken = Uri.UnescapeDataString(issuedUri.Segments[^1]);
        Assert.Equal(Uri.UriSchemeHttps, issuedUri.Scheme);
        Assert.Equal(43, oldToken.Length);
        Assert.DoesNotContain(asset.AssetTag, issued.PayloadUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(asset.Id.ToString(), issued.PayloadUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"SER-{suffix}", issued.PayloadUrl, StringComparison.OrdinalIgnoreCase);

        var svgResponse = await admin.GetAsync($"/api/v1/assets/{asset.Id}/qr");
        svgResponse.EnsureSuccessStatusCode();
        Assert.Equal("image/svg+xml", svgResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            "<svg",
            await svgResponse.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", svgResponse.Headers.CacheControl?.ToString());

        var pngResponse = await admin.GetAsync(
            $"/api/v1/assets/{asset.Id}/qr?format=png");
        pngResponse.EnsureSuccessStatusCode();
        Assert.Equal("image/png", pngResponse.Content.Headers.ContentType?.MediaType);
        var png = await pngResponse.Content.ReadAsByteArrayAsync();
        Assert.True(
            png.Take(8).SequenceEqual(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        Assert.Equal(issued.PayloadUrl, PngQrDecoder.Decode(png));

        var anonymousResolve = await anonymous.GetAsync($"/api/v1/qr/{oldToken}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResolve.StatusCode);

        var resolvedResponse = await auditor.GetAsync($"/api/v1/qr/{oldToken}");
        resolvedResponse.EnsureSuccessStatusCode();
        var resolvedJson = await resolvedResponse.Content.ReadAsStringAsync();
        var resolved = System.Text.Json.JsonSerializer.Deserialize<QrResolutionResponse>(
            resolvedJson,
            ApiTestClient.JsonOptions);
        Assert.Equal(asset.Id, resolved?.Id);
        Assert.DoesNotContain("serialNumber", resolvedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("currentAssignment", resolvedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("purchaseCost", resolvedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(oldToken, resolvedJson, StringComparison.Ordinal);

        var operatorRotation = await operatorClient.PostAsync(
            $"/api/v1/assets/{asset.Id}/qr/rotate",
            content: null);
        Assert.Equal(HttpStatusCode.Forbidden, operatorRotation.StatusCode);

        var auditorIssue = await auditor.PostAsync(
            $"/api/v1/assets/{asset.Id}/qr",
            content: null);
        Assert.Equal(HttpStatusCode.Forbidden, auditorIssue.StatusCode);

        var operatorAsset = await CreateAssetAsync(admin, $"{suffix}-op");
        var operatorIssue = await operatorClient.PostAsync(
            $"/api/v1/assets/{operatorAsset.Id}/qr",
            content: null);
        Assert.Equal(HttpStatusCode.Created, operatorIssue.StatusCode);

        var rotateResponse = await admin.PostAsync(
            $"/api/v1/assets/{asset.Id}/qr/rotate",
            content: null);
        rotateResponse.EnsureSuccessStatusCode();
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<QrIdentifierResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(rotated);
        Assert.Equal(2, rotated.TokenVersion);
        Assert.NotEqual(issued.Id, rotated.Id);
        Assert.NotEqual(issued.PayloadUrl, rotated.PayloadUrl);
        var newToken = Uri.UnescapeDataString(new Uri(rotated.PayloadUrl).Segments[^1]);

        var oldResolve = await auditor.GetAsync($"/api/v1/qr/{oldToken}");
        Assert.Equal(HttpStatusCode.NotFound, oldResolve.StatusCode);
        var oldProblem = await oldResolve.Content.ReadAsStringAsync();
        Assert.DoesNotContain(oldToken, oldProblem, StringComparison.Ordinal);
        Assert.Contains("/api/v1/qr/{token}", oldProblem, StringComparison.Ordinal);

        var newResolve = await auditor.GetAsync($"/api/v1/qr/{newToken}");
        newResolve.EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var identifiers = await dbContext.QrIdentifiers
                .AsNoTracking()
                .Where(identifier => identifier.AssetId == asset.Id)
                .OrderBy(identifier => identifier.TokenVersion)
                .ToArrayAsync();
            Assert.Equal(2, identifiers.Length);
            Assert.NotNull(identifiers[0].RevokedAtUtc);
            Assert.Null(identifiers[1].RevokedAtUtc);
            Assert.All(
                identifiers,
                identifier =>
                {
                    Assert.DoesNotContain(oldToken, identifier.TokenHash, StringComparison.Ordinal);
                    Assert.DoesNotContain(oldToken, identifier.ProtectedToken, StringComparison.Ordinal);
                    Assert.DoesNotContain(newToken, identifier.TokenHash, StringComparison.Ordinal);
                    Assert.DoesNotContain(newToken, identifier.ProtectedToken, StringComparison.Ordinal);
                });
        }

        var history = await admin.GetFromJsonAsync<PagedResponse<AssetEventResponse>>(
            $"/api/v1/assets/{asset.Id}/history",
            ApiTestClient.JsonOptions);
        Assert.Contains(history?.Items ?? [], item => item.Type == "QrIssued");
        Assert.Contains(history?.Items ?? [], item => item.Type == "QrRotated");
        Assert.DoesNotContain(
            oldToken,
            string.Join("", (history?.Items ?? []).Select(item => item.Details.ToString())),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WarrantyLifecycleFeedsAssetSummaryFiltersAndExpiringReport()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var auditor = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);
        using var anonymous = _factory.CreateHttpsClient();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var asset = await CreateAssetAsync(admin, suffix);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(WarrantyStateContract.NotRecorded, asset.WarrantySummary.State);
        var anonymousList = await anonymous.GetAsync(
            $"/api/v1/assets/{asset.Id}/warranties");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);

        var invalidResponse = await admin.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/warranties",
            new CreateWarrantyRequest
            {
                Provider = "Invalid Dates Ltd",
                CoverageType = "Manufacturer",
                StartDate = today,
                EndDate = today.AddDays(-1),
                IsPrimary = true,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        var expiring = await AddWarrantyAsync(
            admin,
            asset.Id,
            new CreateWarrantyRequest
            {
                Provider = "First Cover Ltd",
                CoverageType = "Manufacturer",
                StartDate = today.AddDays(-365),
                EndDate = today.AddDays(30),
                Reference = $"WAR-{suffix}",
                IsPrimary = true,
            });
        Assert.Equal(WarrantyStateContract.ExpiringSoon, expiring.State);
        Assert.Equal(30, expiring.DaysRemaining);

        var assetWithWarranty = await admin.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{asset.Id}",
            ApiTestClient.JsonOptions);
        Assert.Equal(
            WarrantyStateContract.ExpiringSoon,
            assetWithWarranty?.WarrantySummary.State);
        Assert.Equal("First Cover Ltd", assetWithWarranty?.WarrantySummary.Provider);

        var filtered = await admin.GetFromJsonAsync<PagedResponse<AssetListItemResponse>>(
            $"/api/v1/assets?warrantyState=expiringSoon&" +
            $"warrantyEndsBefore={today.AddDays(31):yyyy-MM-dd}&q={asset.AssetTag}",
            ApiTestClient.JsonOptions);
        Assert.Contains(filtered?.Items ?? [], item => item.Id == asset.Id);

        var report = await auditor.GetFromJsonAsync<PagedResponse<WarrantyResponse>>(
            "/api/v1/warranties/expiring?withinDays=31",
            ApiTestClient.JsonOptions);
        Assert.Contains(report?.Items ?? [], item => item.Id == expiring.Id);

        var active = await AddWarrantyAsync(
            admin,
            asset.Id,
            new CreateWarrantyRequest
            {
                Provider = "Replacement Cover Ltd",
                CoverageType = "Extended",
                StartDate = today,
                EndDate = today.AddDays(400),
                IsPrimary = true,
            });
        Assert.Equal(WarrantyStateContract.Active, active.State);

        var warranties = await auditor.GetFromJsonAsync<WarrantyResponse[]>(
            $"/api/v1/assets/{asset.Id}/warranties",
            ApiTestClient.JsonOptions);
        Assert.Equal(2, warranties?.Length);
        Assert.False(warranties?.Single(item => item.Id == expiring.Id).IsPrimary);
        Assert.True(warranties?.Single(item => item.Id == active.Id).IsPrimary);

        var staleResponse = await admin.PutAsJsonAsync(
            $"/api/v1/warranties/{active.Id}",
            ToUpdate(active, Guid.NewGuid()),
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        var expiredRequest = new UpdateWarrantyRequest
        {
            Provider = active.Provider,
            CoverageType = active.CoverageType,
            StartDate = today.AddDays(-400),
            EndDate = today.AddDays(-1),
            Reference = active.Reference,
            Notes = active.Notes,
            IsPrimary = active.IsPrimary,
            Version = active.Version,
        };
        var updateResponse = await admin.PutAsJsonAsync(
            $"/api/v1/warranties/{active.Id}",
            expiredRequest,
            ApiTestClient.JsonOptions);
        updateResponse.EnsureSuccessStatusCode();
        var expired = await updateResponse.Content.ReadFromJsonAsync<WarrantyResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(expired);
        Assert.Equal(WarrantyStateContract.Expired, expired.State);

        var archiveResponse = await admin.PostAsJsonAsync(
            $"/api/v1/warranties/{expired.Id}/archive",
            new VersionRequest(expired.Version),
            ApiTestClient.JsonOptions);
        archiveResponse.EnsureSuccessStatusCode();
        var archived = await archiveResponse.Content.ReadFromJsonAsync<WarrantyResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(archived?.ArchivedAtUtc);
        Assert.False(archived.IsPrimary);

        var afterArchive = await admin.GetFromJsonAsync<AssetResponse>(
            $"/api/v1/assets/{asset.Id}",
            ApiTestClient.JsonOptions);
        Assert.Equal(
            WarrantyStateContract.NotRecorded,
            afterArchive?.WarrantySummary.State);

        var includingArchived = await auditor.GetFromJsonAsync<WarrantyResponse[]>(
            $"/api/v1/assets/{asset.Id}/warranties?includeArchived=true",
            ApiTestClient.JsonOptions);
        Assert.Contains(includingArchived ?? [], item => item.Id == archived.Id);

        var auditorWrite = await auditor.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/warranties",
            new CreateWarrantyRequest
            {
                Provider = "Forbidden Cover Ltd",
                CoverageType = "Extended",
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, auditorWrite.StatusCode);

        var history = await admin.GetFromJsonAsync<PagedResponse<AssetEventResponse>>(
            $"/api/v1/assets/{asset.Id}/history",
            ApiTestClient.JsonOptions);
        Assert.Contains(history?.Items ?? [], item => item.Type == "WarrantyAdded");
        Assert.Contains(history?.Items ?? [], item => item.Type == "WarrantyChanged");
        Assert.Contains(history?.Items ?? [], item => item.Type == "WarrantyArchived");
    }

    private static async Task<AssetResponse> CreateAssetAsync(
        HttpClient client,
        string suffix)
    {
        var categoryResponse = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = $"M3-{suffix}",
                Name = "Milestone 3 category",
            },
            ApiTestClient.JsonOptions);
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Category response was empty.");

        var response = await client.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = $"QR-{suffix}",
                CategoryId = category.Id,
                SerialNumber = $"SER-{suffix}",
                Status = AssetStatusContract.InStock,
                Condition = AssetConditionContract.Good,
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Asset response was empty.");
    }

    private static async Task<WarrantyResponse> AddWarrantyAsync(
        HttpClient client,
        Guid assetId,
        CreateWarrantyRequest request)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/assets/{assetId}/warranties",
            request,
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<WarrantyResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Warranty response was empty.");
    }

    private static UpdateWarrantyRequest ToUpdate(
        WarrantyResponse warranty,
        Guid version) =>
        new()
        {
            Provider = warranty.Provider,
            CoverageType = warranty.CoverageType,
            StartDate = warranty.StartDate,
            EndDate = warranty.EndDate,
            Reference = warranty.Reference,
            Notes = warranty.Notes,
            IsPrimary = warranty.IsPrimary,
            Version = version,
        };
}
