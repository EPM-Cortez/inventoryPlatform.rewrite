using System.Net;
using System.Net.Http.Json;
using Inventory.Api.Security;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.IntegrationTests;

public sealed class HttpPipelineHardeningTests : IClassFixture<InventoryApiFactory>
{
    private const string ForwardedClientIp = "203.0.113.9";
    private const string SpoofedClientIp = "198.51.100.7";

    private readonly InventoryApiFactory _factory;

    public HttpPipelineHardeningTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SecurityHeadersAreOnJsonErrorAndFileResponses()
    {
        using var anonymousClient = _factory.CreateHttpsClient();
        using var liveResponse = await anonymousClient.GetAsync("/health/live");
        using var unauthorizedResponse = await anonymousClient.GetAsync("/api/v1/assets");

        using var auditorClient = await _factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);
        using var csvResponse = await auditorClient.GetAsync("/api/v1/exports/assets.csv");

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, csvResponse.StatusCode);
        foreach (var response in new[] { liveResponse, unauthorizedResponse, csvResponse })
        {
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
            Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
            Assert.Equal(
                "frame-ancestors 'none'",
                Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        }
    }

    [Fact]
    public async Task SpoofedForwardedForHeaderIsIgnoredWithoutAKnownProxy()
    {
        await _factory.GetDatabaseStateAsync();
        using var client = _factory.CreateHttpsClient();
        var csrf = await ApiTestClient.GetCsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", SpoofedClientIp);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = InventoryApiFactory.AuditorEmail,
                password = InventoryApiFactory.AuditorPassword,
                rememberMe = false,
            });
        response.EnsureSuccessStatusCode();

        Assert.DoesNotContain(
            SpoofedClientIp,
            await GetLoginAuditIpsAsync(_factory.Services));
    }

    [Fact]
    public async Task KnownProxyForwardedForBecomesTheAuditedClientIp()
    {
        await _factory.GetDatabaseStateAsync();
        using var proxiedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ForwardedHeaders:Enabled", "true");
            builder.UseSetting("ForwardedHeaders:KnownProxies:0", "127.0.0.1");
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new LoopbackRemoteIpStartupFilter()));
        });
        using var client = proxiedFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
            });
        var csrf = await ApiTestClient.GetCsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ForwardedClientIp);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = InventoryApiFactory.AuditorEmail,
                password = InventoryApiFactory.AuditorPassword,
                rememberMe = false,
            });
        response.EnsureSuccessStatusCode();

        Assert.Contains(
            ForwardedClientIp,
            await GetLoginAuditIpsAsync(proxiedFactory.Services));
    }

    [Fact]
    public async Task ReadinessProbeIsRateLimitedPerClient()
    {
        await _factory.GetDatabaseStateAsync();
        using var limitedFactory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimits:ReadinessPermitLimit", "2"));
        using var client = limitedFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            });

        using var first = await client.GetAsync("/health/ready");
        using var second = await client.GetAsync("/health/ready");
        using var third = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task RequestTelemetryRedactsQrRouteTokens()
    {
        const string token = "opaque-qr-token-that-must-not-be-logged";
        var logger = new CapturingLogger<RequestTelemetryMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = $"/api/v1/qr/{token}";
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        var middleware = new RequestTelemetryMiddleware(
            _ => Task.CompletedTask,
            logger);

        await middleware.InvokeAsync(context, new OperationalMetrics());

        var message = Assert.Single(logger.Messages);
        Assert.Contains("/api/v1/qr/{token}", message, StringComparison.Ordinal);
        Assert.DoesNotContain(token, message, StringComparison.Ordinal);
    }

    private static async Task<string[]> GetLoginAuditIpsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await dbContext.AuditLogs
            .AsNoTracking()
            .Where(audit => audit.Action == "authentication.login" && audit.IpAddress != null)
            .Select(audit => audit.IpAddress!)
            .ToArrayAsync();
    }

    private sealed class LoopbackRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Loopback;
                    await nextMiddleware();
                });
                next(app);
            };
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
