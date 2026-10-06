using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class AuthenticatedRateLimitTests
{
    private static readonly byte[] PdfContent = "%PDF-rate-limit"u8.ToArray();

    [Fact]
    public async Task ImportPreviewRoutesShareAPerUserBudget()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            importPreviewPermitLimit: 3);
        using var administrator = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var manager = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.InventoryManagerEmail,
            InventoryApiFactory.InventoryManagerPassword);

        using var firstContent = new MultipartFormDataContent();
        using var first = await administrator.PostAsync(
            "/api/v1/imports/assets/validate", firstContent);
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);

        using var secondContent = new MultipartFormDataContent();
        using var second = await administrator.PostAsync(
            "/api/v1/imports/people/validate", secondContent);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        using var legacyContent = new MultipartFormDataContent();
        using var legacy = await administrator.PostAsync(
            "/api/v1/legacy-reconciliation/dry-runs", legacyContent);
        Assert.Equal(HttpStatusCode.BadRequest, legacy.StatusCode);

        using var limitedContent = new MultipartFormDataContent();
        using var limited = await administrator.PostAsync(
            "/api/v1/imports/departments/validate", limitedContent);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        using var independentContent = new MultipartFormDataContent();
        using var independent = await manager.PostAsync(
            "/api/v1/imports/locations/validate", independentContent);
        Assert.Equal(HttpStatusCode.BadRequest, independent.StatusCode);
    }

    [Fact]
    public async Task PersistedLegacyDryRunConsumesTheSharedPreviewBudget()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            importPreviewPermitLimit: 1);
        using var administrator = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("bad,header\n"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "legacy-preview.csv");
        content.Add(new StringContent("CreateOnly"), "mode");
        using var created = await administrator.PostAsync(
            "/api/v1/legacy-reconciliation/dry-runs",
            content);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var blockedContent = new MultipartFormDataContent();
        using var blocked = await administrator.PostAsync(
            "/api/v1/imports/assets/validate",
            blockedContent);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(1, await database.AssetImportJobs.CountAsync());
        Assert.Equal(1, await database.LegacyReconciliationRuns.CountAsync());
    }

    [Fact]
    public async Task AuthenticatedUsersOnTheSameIpHaveIndependentUploadLimits()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            attachmentUploadPermitLimit: 1);
        using var administrator = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var technician = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var categoryResponse = await administrator.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = $"RATE-{suffix}",
                Name = $"Rate limit {suffix}",
            },
            ApiTestClient.JsonOptions);
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content
            .ReadFromJsonAsync<CategoryResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(category);
        var assetResponse = await administrator.PostAsJsonAsync(
            "/api/v1/assets",
            new CreateAssetRequest
            {
                AssetTag = $"RATE-{suffix}",
                CategoryId = category.Id,
                Status = AssetStatusContract.InStock,
                Condition = AssetConditionContract.Good,
            },
            ApiTestClient.JsonOptions);
        assetResponse.EnsureSuccessStatusCode();
        var asset = await assetResponse.Content
            .ReadFromJsonAsync<AssetResponse>(ApiTestClient.JsonOptions);
        Assert.NotNull(asset);

        using var firstAdministratorUpload = CreateUpload("admin-one.pdf");
        using var first = await administrator.PostAsync(
            $"/api/v1/assets/{asset.Id}/attachments",
            firstAdministratorUpload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var secondAdministratorUpload = CreateUpload("admin-two.pdf");
        using var limited = await administrator.PostAsync(
            $"/api/v1/assets/{asset.Id}/attachments",
            secondAdministratorUpload);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        using var technicianUpload = CreateUpload("technician.pdf");
        using var independent = await technician.PostAsync(
            $"/api/v1/assets/{asset.Id}/attachments",
            technicianUpload);
        Assert.Equal(HttpStatusCode.Created, independent.StatusCode);
    }

    private static MultipartFormDataContent CreateUpload(string fileName)
    {
        var upload = new MultipartFormDataContent();
        upload.Add(new StringContent("Invoice"), "category");
        var content = new ByteArrayContent(PdfContent);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        upload.Add(content, "file", fileName);
        return upload;
    }
}
