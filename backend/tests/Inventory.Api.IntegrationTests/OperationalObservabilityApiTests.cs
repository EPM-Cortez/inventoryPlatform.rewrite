using System.Net;
using System.Net.Http.Json;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inventory.Api.IntegrationTests;

public sealed class OperationalObservabilityApiTests
{
    [Fact]
    public async Task OperationalStatusExposesCorrelatedMetricsAndThresholdAlertsToAdministrators()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            failedLoginAlertThreshold: 1);
        _ = await factory.GetDatabaseStateAsync();
        using var failedLoginClient = factory.CreateHttpsClient();
        var csrf = await ApiTestClient.GetCsrfTokenAsync(failedLoginClient);
        failedLoginClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        var failedLogin = await failedLoginClient.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = InventoryApiFactory.AdministratorEmail,
                password = "Incorrect-Administrator-Password!42",
                rememberMe = false,
            });
        Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);
        Assert.False(
            string.IsNullOrWhiteSpace(
                Assert.Single(failedLogin.Headers.GetValues("X-Correlation-ID"))));

        using var administrator = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var response = await administrator.GetAsync("/api/v1/operations/status");
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<OperationalStatusResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(status);
        Assert.True(status.Metrics.FailedLogins >= 1);
        Assert.False(status.Storage.Configured);
        Assert.False(status.Storage.DatabaseConfigured);
        Assert.Contains(
            status.Alerts,
            alert => alert.Code == "failed_login_threshold");

        using var technician = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await technician.GetAsync("/api/v1/operations/status")).StatusCode);
    }

    [Fact]
    public async Task DatabaseVolumeCapacityIsExposedAndFailsReadinessBelowItsFloor()
    {
        const long availableBytes = 32L * 1024 * 1024;
        using var baseFactory = new InventoryApiFactory();
        _ = await baseFactory.GetDatabaseStateAsync();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "Storage:MinimumDatabaseFreeSpaceBytes",
                (64L * 1024 * 1024).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DatabaseStorageLocation>();
                services.RemoveAll<IStorageVolumeProbe>();
                services.AddSingleton(
                    new DatabaseStorageLocation(
                        Path.Combine(
                            Path.GetTempPath(),
                            "inventory-volume-test",
                            "inventory.db")));
                services.AddSingleton<IStorageVolumeProbe>(
                    new FixedStorageVolumeProbe(availableBytes));
            });
        });
        using var anonymous = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
            });

        using var readiness = await anonymous.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);

        using var administrator = await CreateAuthenticatedClientAsync(factory);
        var response = await administrator.GetAsync("/api/v1/operations/status");
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<OperationalStatusResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(status);
        Assert.True(status.Storage.DatabaseConfigured);
        Assert.Equal(
            availableBytes,
            status.Storage.DatabaseAvailableFreeSpaceBytes);
        Assert.Contains(
            status.Alerts,
            alert =>
                alert.Severity == "critical" &&
                alert.Code == "database_storage_capacity_low");
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
            });
        var csrf = await ApiTestClient.GetCsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = InventoryApiFactory.AdministratorEmail,
                password = InventoryApiFactory.AdministratorPassword,
                rememberMe = false,
            });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        csrf = await ApiTestClient.GetCsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        return client;
    }

    private sealed class FixedStorageVolumeProbe : IStorageVolumeProbe
    {
        private readonly long _availableBytes;

        public FixedStorageVolumeProbe(long availableBytes)
        {
            _availableBytes = availableBytes;
        }

        public long GetAvailableFreeSpaceBytes(string path) => _availableBytes;
    }
}
