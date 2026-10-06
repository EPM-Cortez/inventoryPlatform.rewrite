using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class ImportPreviewBudgetTests
{
    private static readonly byte[] InvalidCsv = Encoding.UTF8.GetBytes(
        "unexpected-header\nvalue\n");

    [Fact]
    public async Task AllFivePreviewRoutesShareOneActiveJobLimit()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            importPreviewMaximumJobs: 4);
        using var client = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var routes = new[]
        {
            ("/api/v1/imports/assets/validate", "CreateOnly"),
            ("/api/v1/imports/people/validate", "CreateOnly"),
            ("/api/v1/imports/departments/validate", "CreateOnly"),
            ("/api/v1/imports/locations/validate", "CreateOnly"),
        };
        foreach (var (route, mode) in routes)
        {
            using var response = await UploadAsync(client, route, mode);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        using var blocked = await UploadAsync(
            client,
            "/api/v1/legacy-reconciliation/dry-runs",
            "CreateOnly");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains(
            "import_preview_budget_exceeded",
            await blocked.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(1, await database.AssetImportJobs.CountAsync());
        Assert.Equal(3, await database.ReferenceDataImportJobs.CountAsync());
        Assert.Equal(0, await database.LegacyReconciliationRuns.CountAsync());
        Assert.Equal(4, await database.ImportPreviewReservations.CountAsync());
    }

    [Fact]
    public async Task CumulativeByteLimitRejectsSecondPreviewWithoutPartialJob()
    {
        int payloadBytes;
        using (var measuringFactory = new InventoryApiFactory())
        {
            using var measuringClient = await measuringFactory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
            using var measured = await UploadAsync(
                measuringClient,
                "/api/v1/imports/assets/validate",
                "CreateOnly");
            Assert.Equal(HttpStatusCode.Created, measured.StatusCode);
            await using var measuringScope = measuringFactory.Services.CreateAsyncScope();
            var measuringDatabase = measuringScope.ServiceProvider
                .GetRequiredService<InventoryDbContext>();
            payloadBytes = (await measuringDatabase.ImportPreviewReservations.SingleAsync())
                .PayloadBytes;
        }

        Assert.True(payloadBytes > 1);
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            importPreviewMaximumBytesPerUser: payloadBytes * 2L - 1);
        using var client = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var first = await UploadAsync(
            client,
            "/api/v1/imports/assets/validate",
            "CreateOnly");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await UploadAsync(
            client,
            "/api/v1/imports/assets/validate",
            "CreateOnly");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains(
            "import_preview_budget_exceeded",
            await second.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(1, await database.AssetImportJobs.CountAsync());
        Assert.Equal(1, await database.ImportPreviewReservations.CountAsync());
    }

    [Fact]
    public async Task SqliteMigrationBackfillsExistingPreviewReservation()
    {
        using var factory = new InventoryApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var response = await UploadAsync(
            client,
            "/api/v1/imports/assets/validate",
            "CreateOnly");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await database.Database.MigrateAsync("20261006204911_AddPendingMfaChallenges");
        Assert.Equal(1, await database.AssetImportJobs.CountAsync());
        await database.Database.MigrateAsync();
        var reservation = await database.ImportPreviewReservations
            .SingleAsync(item => item.PayloadId == job.Id);
        Assert.True(reservation.PayloadBytes > 0);
        Assert.Equal(
            TimeSpan.FromDays(7),
            reservation.ExpiresAtUtc - reservation.CreatedAtUtc);
        using var readable = await client.GetAsync($"/api/v1/imports/{job.Id}");
        Assert.Equal(HttpStatusCode.OK, readable.StatusCode);
    }

    [Fact]
    public async Task ExpiredPayloadsRejectReadsAndArePurgedWithoutDeletingAudit()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            utcNow: new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        using var client = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var response = await UploadAsync(
            client,
            "/api/v1/imports/assets/validate",
            "CreateOnly");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<AssetImportJobResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(job);

        factory.AdvanceClock(TimeSpan.FromDays(8));
        using var expired = await client.GetAsync($"/api/v1/imports/{job.Id}");
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        Assert.Contains(
            "import_preview_expired",
            await expired.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
        using var errorReport = await client.GetAsync(
            $"/api/v1/imports/{job.Id}/errors.csv");
        Assert.Equal(HttpStatusCode.Conflict, errorReport.StatusCode);
        using var commit = await client.PostAsJsonAsync(
            $"/api/v1/imports/{job.Id}/commit",
            new CommitAssetImportRequest { Version = job.Version });
        Assert.Equal(HttpStatusCode.Conflict, commit.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var budget = scope.ServiceProvider.GetRequiredService<EfImportPreviewBudget>();
        Assert.Equal(1, await budget.PurgeExpiredAsync(CancellationToken.None));
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var persisted = await database.AssetImportJobs.SingleAsync();
        Assert.Equal("[]", persisted.ValidatedRowsJson);
        Assert.Equal("[]", persisted.ErrorsJson);
        Assert.Equal(1, await database.AuditLogs.CountAsync(
            audit => audit.Action == "import.preview.expired"));
        Assert.Equal(1, await database.AssetImportJobs.CountAsync());
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        string route,
        string mode)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(InvalidCsv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "preview.csv");
        form.Add(new StringContent(mode), "mode");
        return await client.PostAsync(route, form);
    }
}
