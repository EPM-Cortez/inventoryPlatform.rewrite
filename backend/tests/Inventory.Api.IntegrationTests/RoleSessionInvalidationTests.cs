using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Security;
using Inventory.Contracts.V1;

namespace Inventory.Api.IntegrationTests;

public sealed class RoleSessionInvalidationTests
{
    [Fact]
    public async Task RoleChangeImmediatelyInvalidatesExistingSessionAndNewPermissionsApply()
    {
        using var factory = new InventoryApiFactory();
        var unique = Guid.NewGuid().ToString("N");
        var email = $"session-role-{unique}@inventory.test";
        const string password = "Session-Role-Password!42";
        await factory.CreateUserWithCustomRoleAsync(
            email,
            password,
            RoleNames.Technician,
            []);

        using var administrator = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var technician = await factory.CreateAuthenticatedClientAsync(email, password);
        var beforeRoleChange = await technician.PostAsJsonAsync(
            "/api/v1/people",
            new
            {
                externalId = $"ROLE-{unique}",
                displayName = "Before role change",
                type = "staff",
                email = (string?)null,
            });
        Assert.Equal(HttpStatusCode.Created, beforeRoleChange.StatusCode);

        var users = await administrator.GetFromJsonAsync<PagedResponse<UserResponse>>(
            "/api/v1/users?page=1&pageSize=200",
            ApiTestClient.JsonOptions);
        var user = Assert.Single(users!.Items, item => item.Email == email);
        var roleChange = await administrator.PutAsJsonAsync(
            $"/api/v1/users/{user.Id}/roles",
            new
            {
                roles = new[] { RoleNames.ReadOnlyAuditor },
                version = user.Version,
            });
        roleChange.EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await technician.GetAsync("/api/v1/assets")).StatusCode);

        using var refreshed = await factory.CreateAuthenticatedClientAsync(email, password);
        Assert.Equal(
            HttpStatusCode.OK,
            (await refreshed.GetAsync("/api/v1/assets")).StatusCode);
        var deniedWrite = await refreshed.PostAsJsonAsync(
            "/api/v1/people",
            new
            {
                externalId = $"ROLE-AFTER-{unique}",
                displayName = "After role change",
                type = "staff",
                email = (string?)null,
            });
        Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
    }
}
