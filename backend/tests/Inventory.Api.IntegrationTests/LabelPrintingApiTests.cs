using System.Net;
using System.Net.Http.Json;
using System.Text;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class LabelPrintingApiTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public LabelPrintingApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PrintOnlyPermissionCannotIssueMissingQrIdentifiers()
    {
        const string printOnlyEmail = "print-only@inventory.test";
        const string printOnlyPassword = "Print-Only-Password!42";
        await _factory.CreateUserWithCustomRoleAsync(
            printOnlyEmail,
            printOnlyPassword,
            "PrintOnly",
            ["assets.read", "qr.print"]);
        using var admin = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var printOnly = await _factory.CreateAuthenticatedClientAsync(
            printOnlyEmail,
            printOnlyPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var asset = await CreateAssetAsync(admin, $"po-{suffix}", "HP", "EliteBook 840");

        var denied = await printOnly.PostAsJsonAsync(
            "/api/v1/labels/batch",
            new PrintLabelBatchRequest
            {
                Stock = LabelStockContract.BrotherDk11209,
                Items = [new LabelBatchItemRequest { AssetId = asset.Id, Quantity = 1 }],
                IssueMissingQr = true,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var problem = await denied.Content.ReadFromJsonAsync<ProblemDetailsPayload>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal(
            "https://inventory.local/problems/permission-denied",
            problem.Type);

        var issueResponse = await admin.PostAsync(
            $"/api/v1/assets/{asset.Id}/qr",
            content: null);
        issueResponse.EnsureSuccessStatusCode();

        var allowed = await printOnly.PostAsJsonAsync(
            "/api/v1/labels/batch",
            new PrintLabelBatchRequest
            {
                Stock = LabelStockContract.BrotherDk11209,
                Items = [new LabelBatchItemRequest { AssetId = asset.Id, Quantity = 1 }],
                IssueMissingQr = false,
            },
            ApiTestClient.JsonOptions);
        allowed.EnsureSuccessStatusCode();
        Assert.Equal(
            "application/pdf",
            allowed.Content.Headers.ContentType?.MediaType);
    }

    private sealed record ProblemDetailsPayload(string? Type, string? Title, int? Status);

    [Fact]
    public async Task IndividualAndBatchPrintingUsePhysicalStockSizesAndAreAudited()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var technician = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        using var auditor = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var first = await CreateAssetAsync(
            admin,
            suffix,
            "Lenovo",
            "ThinkPad L14");
        var second = await CreateAssetAsync(
            admin,
            $"{suffix}-2",
            "Dell",
            "Latitude 5450");

        var stocks = await technician.GetFromJsonAsync<LabelStockResponse[]>(
            "/api/v1/labels/stocks",
            ApiTestClient.JsonOptions);
        Assert.NotNull(stocks);
        var rollStock = Assert.Single(
            stocks,
            stock => stock.Stock == LabelStockContract.BrotherDk11209);
        Assert.Equal(62m, rollStock.LabelWidthMillimetres);
        Assert.Equal(29m, rollStock.LabelHeightMillimetres);
        Assert.Equal(1, rollStock.LabelsPerPage);
        var sheetStock = Assert.Single(
            stocks,
            stock => stock.Stock == LabelStockContract.AveryL7159);
        Assert.Equal(24, sheetStock.LabelsPerPage);
        Assert.Equal(3, sheetStock.Columns);
        Assert.Equal(8, sheetStock.Rows);
        Assert.True(sheetStock.SupportsStartPosition);

        var auditorStocks = await auditor.GetAsync("/api/v1/labels/stocks");
        Assert.Equal(HttpStatusCode.Forbidden, auditorStocks.StatusCode);

        var individualResponse = await technician.PostAsJsonAsync(
            $"/api/v1/assets/{first.Id}/label",
            new PrintAssetLabelRequest
            {
                Stock = LabelStockContract.BrotherDk11209,
                Copies = 2,
            },
            ApiTestClient.JsonOptions);
        individualResponse.EnsureSuccessStatusCode();
        Assert.Equal(
            "application/pdf",
            individualResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", individualResponse.Headers.CacheControl?.ToString());
        Assert.Equal(
            "inline",
            individualResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("2", individualResponse.Headers.GetValues("X-Label-Count").Single());
        Assert.Equal(
            "2",
            individualResponse.Headers.GetValues("X-Label-Page-Count").Single());
        var individualPdf = await individualResponse.Content.ReadAsByteArrayAsync();
        AssertPdf(individualPdf, expectedPages: 2, "175.748", "82.205");
        var individualText = Encoding.Latin1.GetString(individualPdf);
        Assert.Contains(first.AssetTag, individualText, StringComparison.Ordinal);
        Assert.Contains(first.DeviceName!, individualText, StringComparison.Ordinal);
        Assert.Contains("/PrintScaling /None", individualText, StringComparison.Ordinal);

        var batchResponse = await technician.PostAsJsonAsync(
            "/api/v1/labels/batch",
            new PrintLabelBatchRequest
            {
                Stock = LabelStockContract.AveryL7159,
                StartPosition = 23,
                Items =
                [
                    new LabelBatchItemRequest
                    {
                        AssetId = first.Id,
                        Quantity = 2,
                    },
                    new LabelBatchItemRequest
                    {
                        AssetId = second.Id,
                        Quantity = 2,
                    },
                ],
            },
            ApiTestClient.JsonOptions);
        batchResponse.EnsureSuccessStatusCode();
        Assert.Equal("4", batchResponse.Headers.GetValues("X-Label-Count").Single());
        Assert.Equal(
            "2",
            batchResponse.Headers.GetValues("X-Label-Page-Count").Single());
        var batchPdf = await batchResponse.Content.ReadAsByteArrayAsync();
        AssertPdf(batchPdf, expectedPages: 2, "595.276", "841.89");
        var batchText = Encoding.Latin1.GetString(batchPdf);
        Assert.Contains(first.AssetTag, batchText, StringComparison.Ordinal);
        Assert.Contains(second.AssetTag, batchText, StringComparison.Ordinal);

        var auditorPrint = await auditor.PostAsJsonAsync(
            $"/api/v1/assets/{first.Id}/label",
            new PrintAssetLabelRequest(),
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, auditorPrint.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var identifiers = await dbContext.QrIdentifiers
            .AsNoTracking()
            .Where(identifier =>
                identifier.AssetId == first.Id ||
                identifier.AssetId == second.Id)
            .ToArrayAsync();
        Assert.Equal(2, identifiers.Length);
        var audits = await dbContext.AuditLogs
            .AsNoTracking()
            .Where(audit => audit.Action == "qr.labels.printed")
            .Where(audit =>
                audit.DetailsJson != null &&
                audit.DetailsJson.Contains(first.Id.ToString()))
            .ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.All(
            audits,
            audit => Assert.DoesNotContain("PayloadUrl", audit.DetailsJson!));
        Assert.All(
            audits,
            audit => Assert.DoesNotContain("ProtectedToken", audit.DetailsJson!));
    }

    [Fact]
    public async Task BatchPrintingRejectsUnsafeBoundsAndPrevalidatesAssetsBeforeIssuingQr()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var asset = await CreateAssetAsync(admin, suffix, null, null);

        var tooMany = await admin.PostAsJsonAsync(
            "/api/v1/labels/batch",
            new PrintLabelBatchRequest
            {
                Items =
                [
                    new LabelBatchItemRequest
                    {
                        AssetId = asset.Id,
                        Quantity = 100,
                    },
                    new LabelBatchItemRequest
                    {
                        AssetId = asset.Id,
                        Quantity = 100,
                    },
                    new LabelBatchItemRequest
                    {
                        AssetId = asset.Id,
                        Quantity = 41,
                    },
                ],
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        var invalidRollStart = await admin.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/label",
            new PrintAssetLabelRequest
            {
                Stock = LabelStockContract.BrotherDk11209,
                StartPosition = 2,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRollStart.StatusCode);

        var noAutoIssue = await admin.PostAsJsonAsync(
            $"/api/v1/assets/{asset.Id}/label",
            new PrintAssetLabelRequest
            {
                IssueMissingQr = false,
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, noAutoIssue.StatusCode);

        var missingAsset = Guid.NewGuid();
        var missingBatch = await admin.PostAsJsonAsync(
            "/api/v1/labels/batch",
            new PrintLabelBatchRequest
            {
                Items =
                [
                    new LabelBatchItemRequest { AssetId = asset.Id },
                    new LabelBatchItemRequest { AssetId = missingAsset },
                ],
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, missingBatch.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.False(
            await dbContext.QrIdentifiers
                .AsNoTracking()
                .AnyAsync(identifier => identifier.AssetId == asset.Id));
    }

    private static void AssertPdf(
        byte[] content,
        int expectedPages,
        string expectedWidth,
        string expectedHeight)
    {
        Assert.True(content.Length > 1_000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(content, 0, 5));
        var text = Encoding.Latin1.GetString(content);
        Assert.Contains(
            $"/MediaBox [0 0 {expectedWidth} {expectedHeight}]",
            text,
            StringComparison.Ordinal);
        Assert.Equal(
            expectedPages,
            text.Split("/Type /Page ", StringSplitOptions.None).Length - 1);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
    }

    private static async Task<AssetResponse> CreateAssetAsync(
        HttpClient client,
        string suffix,
        string? manufacturer,
        string? model)
    {
        var categoryResponse = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new CreateCategoryRequest
            {
                Code = $"LBL-{suffix}",
                Name = "Printable devices",
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
                AssetTag = $"LBL-{suffix}",
                DeviceName = $"DEVICE-{suffix}",
                CategoryId = category.Id,
                Manufacturer = manufacturer,
                Model = model,
                Status = AssetStatusContract.InStock,
                Condition = AssetConditionContract.Good,
            },
            ApiTestClient.JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssetResponse>(
            ApiTestClient.JsonOptions)
            ?? throw new InvalidOperationException("Asset response was empty.");
    }
}
