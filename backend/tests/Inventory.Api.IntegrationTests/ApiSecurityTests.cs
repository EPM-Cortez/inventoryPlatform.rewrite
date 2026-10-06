using System.Net;
using System.Net.Http.Json;

namespace Inventory.Api.IntegrationTests;

public sealed class ApiSecurityTests : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public ApiSecurityTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AnonymousBusinessRequestReturns401()
    {
        using var client = _factory.CreateHttpsClient();

        var response = await client.GetAsync("/api/v1/assets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task LoginWithoutAntiforgeryTokenReturns400()
    {
        using var client = _factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = InventoryApiFactory.AdministratorEmail,
                password = InventoryApiFactory.AdministratorPassword,
                rememberMe = false,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuditorCannotCreateReferenceData()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);

        var response = await client.PostAsJsonAsync(
            "/api/v1/categories",
            new { code = $"AUD-{Guid.NewGuid():N}", name = "Denied category" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DisabledUserCookieIsRejectedOnTheNextRequest()
    {
        using var client = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        var beforeDisable = await client.GetAsync("/api/v1/assets");
        Assert.Equal(HttpStatusCode.OK, beforeDisable.StatusCode);

        await _factory.DisableUserAsync(InventoryApiFactory.TechnicianEmail);

        var afterDisable = await client.GetAsync("/api/v1/assets");
        Assert.Equal(HttpStatusCode.Unauthorized, afterDisable.StatusCode);
    }

    [Fact]
    public async Task TestDatabaseUsesTheProductionSqliteProviderAndMigrations()
    {
        var state = await _factory.GetDatabaseStateAsync();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", state.ProviderName);
        Assert.Contains(
            state.AppliedMigrations,
            migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
        Assert.Contains(
            state.AppliedMigrations,
            migration => migration.EndsWith(
                "_AddPeopleDepartmentsAndAssignments",
                StringComparison.Ordinal));
        Assert.Empty(state.PendingMigrations);
        Assert.True(state.ForeignKeysEnabled);
    }

    [Fact]
    public async Task ReadinessIsHealthyAfterSqliteMigrationsAreApplied()
    {
        await _factory.GetDatabaseStateAsync();
        using var client = _factory.CreateHttpsClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
