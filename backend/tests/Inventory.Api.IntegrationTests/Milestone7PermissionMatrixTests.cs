using System.Net;
using System.Net.Http.Json;

namespace Inventory.Api.IntegrationTests;

public sealed class Milestone7PermissionMatrixTests
    : IClassFixture<InventoryApiFactory>
{
    private static readonly string[] AllRoles =
    [
        "Administrator",
        "InventoryManager",
        "Technician",
        "ReadOnlyAuditor",
    ];

    private static readonly string[] AdministratorsAndManagers =
    [
        "Administrator",
        "InventoryManager",
    ];

    private static readonly string[] OperationalRoles =
    [
        "Administrator",
        "InventoryManager",
        "Technician",
    ];

    private readonly InventoryApiFactory _factory;

    public Milestone7PermissionMatrixTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PresetRolesEnforceEveryMilestone7PermissionBoundary()
    {
        var clients = new Dictionary<string, HttpClient>(StringComparer.Ordinal)
        {
            ["Administrator"] =
                await _factory.CreateAuthenticatedClientAsync(
                    InventoryApiFactory.AdministratorEmail,
                    InventoryApiFactory.AdministratorPassword),
            ["InventoryManager"] =
                await _factory.CreateAuthenticatedClientAsync(
                    InventoryApiFactory.InventoryManagerEmail,
                    InventoryApiFactory.InventoryManagerPassword),
            ["Technician"] =
                await _factory.CreateAuthenticatedClientAsync(
                    InventoryApiFactory.TechnicianEmail,
                    InventoryApiFactory.TechnicianPassword),
            ["ReadOnlyAuditor"] =
                await _factory.CreateAuthenticatedClientAsync(
                    InventoryApiFactory.AuditorEmail,
                    InventoryApiFactory.AuditorPassword),
        };
        try
        {
            var missingAssetId = Guid.NewGuid();
            var missingAttachmentId = Guid.NewGuid();
            var cases = new[]
            {
                Read(
                    "stocktakes.read",
                    "/api/v1/stocktakes",
                    AllRoles),
                Write(
                    "stocktakes.manage",
                    HttpMethod.Post,
                    "/api/v1/stocktakes",
                    new Dictionary<string, object?>(),
                    OperationalRoles),
                Read(
                    "asset-tags.manage",
                    "/api/v1/settings/asset-tags",
                    AdministratorsAndManagers),
                Write(
                    "assets.bulk",
                    HttpMethod.Post,
                    "/api/v1/assets/bulk-changes/preview",
                    new Dictionary<string, object?>(),
                    AdministratorsAndManagers),
                Read(
                    "bundles.read",
                    "/api/v1/asset-bundles",
                    AllRoles),
                Write(
                    "bundles.manage",
                    HttpMethod.Post,
                    "/api/v1/asset-bundles",
                    new Dictionary<string, object?>(),
                    OperationalRoles),
                Read(
                    "leavers.read",
                    "/api/v1/leavers",
                    AllRoles),
                Write(
                    "leavers.manage",
                    HttpMethod.Post,
                    "/api/v1/leavers",
                    new Dictionary<string, object?>(),
                    AdministratorsAndManagers),
                Read(
                    "attachments.read",
                    $"/api/v1/assets/{missingAssetId}/attachments",
                    AllRoles),
                Write(
                    "attachments.manage",
                    HttpMethod.Post,
                    $"/api/v1/assets/{missingAssetId}/attachments/" +
                    $"{missingAttachmentId}/archive",
                    new { version = Guid.NewGuid() },
                    OperationalRoles),
            };

            foreach (var permissionCase in cases)
            {
                foreach (var (role, client) in clients)
                {
                    using var request = new HttpRequestMessage(
                        permissionCase.Method,
                        permissionCase.Path);
                    if (permissionCase.Body is not null)
                    {
                        request.Content = JsonContent.Create(
                            permissionCase.Body,
                            options: ApiTestClient.JsonOptions);
                    }

                    using var response = await client.SendAsync(request);
                    var expectedAllowed =
                        permissionCase.AllowedRoles.Contains(
                            role,
                            StringComparer.Ordinal);
                    if (expectedAllowed)
                    {
                        Assert.True(
                            response.StatusCode is not
                                HttpStatusCode.Unauthorized and not
                                HttpStatusCode.Forbidden,
                            $"{permissionCase.Permission} unexpectedly denied {role}.");
                    }
                    else
                    {
                        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                    }
                }
            }
        }
        finally
        {
            foreach (var client in clients.Values)
            {
                client.Dispose();
            }
        }
    }

    private static PermissionEndpointCase Read(
        string permission,
        string path,
        IReadOnlyList<string> allowedRoles) =>
        new(permission, HttpMethod.Get, path, null, allowedRoles);

    private static PermissionEndpointCase Write(
        string permission,
        HttpMethod method,
        string path,
        object body,
        IReadOnlyList<string> allowedRoles) =>
        new(permission, method, path, body, allowedRoles);

    private sealed record PermissionEndpointCase(
        string Permission,
        HttpMethod Method,
        string Path,
        object? Body,
        IReadOnlyList<string> AllowedRoles);
}
