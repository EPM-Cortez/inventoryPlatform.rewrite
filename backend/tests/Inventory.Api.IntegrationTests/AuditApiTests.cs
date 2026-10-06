using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class AuditApiTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public AuditApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AdministratorCanFilterAuditRecordsForAKnownAction()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var unique = Guid.NewGuid().ToString("N");
        var email = $"audited-{unique}@inventory.test";
        var createResponse = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new
            {
                email,
                displayName = "Audited User",
                temporaryPassword = "Audited-User-Password!42",
                roles = new[] { RoleNames.ReadOnlyAuditor },
            });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<UserResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(created);

        var page = await admin.GetFromJsonAsync<PagedResponse<AuditLogResponse>>(
            $"/api/v1/audit?action=user.created&targetType=user&targetId={created.Id}",
            ApiTestClient.JsonOptions);

        Assert.NotNull(page);
        var record = Assert.Single(page.Items);
        Assert.Equal("user.created", record.Action);
        Assert.Equal("user", record.TargetType);
        Assert.Equal(created.Id.ToString(), record.TargetId);
        Assert.Equal("succeeded", record.Outcome);
        Assert.NotNull(record.ActorUserId);
        Assert.Contains(email, record.DetailsJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditorCanReadTheAuditLogButTechnicianCannot()
    {
        using var auditor = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);
        var auditorPage = await auditor.GetAsync("/api/v1/audit");
        Assert.Equal(HttpStatusCode.OK, auditorPage.StatusCode);
        var auditorVocabulary = await auditor.GetAsync(
            "/api/v1/audit/vocabulary");
        Assert.Equal(HttpStatusCode.OK, auditorVocabulary.StatusCode);

        using var technician = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        var denied = await technician.GetAsync("/api/v1/audit");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var deniedVocabulary = await technician.GetAsync(
            "/api/v1/audit/vocabulary");
        Assert.Equal(HttpStatusCode.Forbidden, deniedVocabulary.StatusCode);
    }

    [Fact]
    public async Task AuditQueriesValidateDateRangeAndPaging()
    {
        using var admin = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);

        var invalidRange = await admin.GetAsync(
            "/api/v1/audit?from=2026-07-25T00:00:00Z&to=2026-07-24T00:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, invalidRange.StatusCode);

        var invalidPageSize = await admin.GetAsync("/api/v1/audit?pageSize=201");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageSize.StatusCode);

        var paged = await admin.GetFromJsonAsync<PagedResponse<AuditLogResponse>>(
            "/api/v1/audit?action=authentication.login&page=1&pageSize=1",
            ApiTestClient.JsonOptions);
        Assert.NotNull(paged);
        Assert.Equal(1, paged.PageSize);
        Assert.True(paged.TotalCount >= 1);
        _ = Assert.Single(paged.Items);
        Assert.Equal("authentication.login", paged.Items[0].Action);

        var newestFirst = await admin.GetFromJsonAsync<PagedResponse<AuditLogResponse>>(
            "/api/v1/audit?page=1&pageSize=50",
            ApiTestClient.JsonOptions);
        Assert.NotNull(newestFirst);
        var timestamps = newestFirst.Items.Select(item => item.OccurredAtUtc).ToArray();
        Assert.Equal(timestamps.OrderByDescending(value => value), timestamps);
    }

    [Fact]
    public async Task CanonicalAuditFiltersIncludeLegacySpellings()
    {
        await _factory.GetDatabaseStateAsync();
        var targetId = Guid.NewGuid().ToString();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<InventoryDbContext>();
            var now = DateTimeOffset.UtcNow;
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO "AuditLogs"
                 (
                     "Id", "ActorUserId", "ActorDisplayName", "Action",
                     "TargetType", "TargetId", "OccurredAtUtc", "Outcome",
                     "DetailsJson", "IpAddress", "Client", "CorrelationId"
                 )
                 VALUES
                 (
                     {Guid.NewGuid()}, NULL, 'Legacy importer',
                     'saved-search.create', 'assetImportJob', {targetId},
                     {now.AddSeconds(-1)}, 'success', NULL, NULL, NULL, NULL
                 ),
                 (
                     {Guid.NewGuid()}, NULL, 'Current importer',
                     'saved_search.created', 'asset_import_job', {targetId},
                     {now}, 'succeeded', NULL, NULL, NULL, NULL
                 );
                 """);
        }

        using var admin = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var page = await admin.GetFromJsonAsync<
            PagedResponse<AuditLogResponse>>(
            "/api/v1/audit?action=saved_search.created" +
            "&targetType=asset_import_job&outcome=succeeded" +
            $"&targetId={targetId}",
            ApiTestClient.JsonOptions);

        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.All(
            page.Items,
            item =>
            {
                Assert.Equal("saved_search.created", item.Action);
                Assert.Equal("asset_import_job", item.TargetType);
                Assert.Equal("succeeded", item.Outcome);
            });

        var legacyFilter = await admin.GetFromJsonAsync<
            PagedResponse<AuditLogResponse>>(
            "/api/v1/audit?outcome=success" +
            $"&targetId={targetId}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(legacyFilter);
        Assert.Equal(2, legacyFilter.TotalCount);
        Assert.All(
            legacyFilter.Items,
            item => Assert.Equal("succeeded", item.Outcome));

        var vocabulary = await admin.GetFromJsonAsync<
            AuditFilterVocabularyResponse>(
            "/api/v1/audit/vocabulary",
            ApiTestClient.JsonOptions);
        Assert.NotNull(vocabulary);
        Assert.Contains("saved_search.created", vocabulary.Actions);
        Assert.DoesNotContain("saved-search.create", vocabulary.Actions);
        Assert.Contains("asset_import_job", vocabulary.TargetTypes);
        Assert.DoesNotContain("assetImportJob", vocabulary.TargetTypes);
        Assert.Contains("succeeded", vocabulary.Outcomes);
        Assert.DoesNotContain("success", vocabulary.Outcomes);
    }
}
