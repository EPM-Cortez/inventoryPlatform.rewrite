using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Application.Common;
using Inventory.Application.Identity;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class IdentityAdministrationApiTests
{
    [Fact]
    public async Task AdministratorSeedIsSecureIdempotentAndRefusesASecondAccount()
    {
        using var factory = new InventoryApiFactory(seedDefaultUsers: false);
        const string email = "seed-admin@inventory.test";
        const string password = "Seed-Administrator-Password!42";

        await factory.BootstrapAdministratorAsync(
            email,
            "Seed Administrator",
            password);
        await factory.BootstrapAdministratorAsync(
            email,
            "Seed Administrator",
            password);

        using var client = await factory.CreateAuthenticatedClientAsync(email, password);
        var listResponse = await client.GetAsync("/api/v1/users");
        listResponse.EnsureSuccessStatusCode();
        var users = await listResponse.Content
            .ReadFromJsonAsync<PagedResponse<UserResponse>>(ApiTestClient.JsonOptions);

        Assert.Equal(1, users!.TotalCount);
        var administrator = Assert.Single(users.Items);
        Assert.Equal(email, administrator.Email);
        Assert.True(administrator.IsEnabled);
        Assert.False(administrator.MustChangePassword);
        Assert.Null(administrator.ArchivedAtUtc);
        Assert.NotEqual(Guid.Empty, administrator.Version);
        Assert.Equal([RoleNames.Administrator], administrator.Roles);

        var conflict = await Assert.ThrowsAsync<ResourceConflictException>(
            () => factory.BootstrapAdministratorAsync(
                "second-admin@inventory.test",
                "Second Administrator",
                "Second-Administrator-Password!42"));
        Assert.Equal("administrator_already_exists", conflict.Code);
    }

    [Fact]
    public async Task AdministratorCanCreateEditArchiveAndRestoreTechnicianOrAdminAccounts()
    {
        using var factory = new InventoryApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);

        var presetsResponse = await client.GetAsync("/api/v1/users/role-presets");
        presetsResponse.EnsureSuccessStatusCode();
        var presets = await presetsResponse.Content.ReadFromJsonAsync<RolePresetResponse[]>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(presets);
        var technicianPreset = Assert.Single(
            presets,
            preset => preset.Name == RoleNames.Technician);
        Assert.Contains(Permissions.AssignmentsManage, technicianPreset.Permissions);
        Assert.DoesNotContain(Permissions.UsersManage, technicianPreset.Permissions);
        Assert.DoesNotContain(
            presets,
            preset => preset.Name == RoleNames.LegacyInventoryOperator);

        var unique = Guid.NewGuid().ToString("N");
        var originalEmail = $"technician-{unique}@inventory.test";
        var editedEmail = $"admin-{unique}@inventory.test";
        const string temporaryPassword = "Temporary-Account-Password!42";
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/users",
            new
            {
                email = originalEmail,
                displayName = "New Technician",
                temporaryPassword,
                roles = new[] { RoleNames.Technician },
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<UserResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(created);
        Assert.Equal([RoleNames.Technician], created.Roles);
        Assert.True(created.MustChangePassword);
        Assert.NotEqual(Guid.Empty, created.Version);
        Assert.Equal(
            $"/api/v1/users/{created.Id}",
            createResponse.Headers.Location?.AbsolutePath);

        var missingVersionResponse = await client.PostAsJsonAsync(
            $"/api/v1/users/{created.Id}/disable",
            new { });
        Assert.Equal(HttpStatusCode.BadRequest, missingVersionResponse.StatusCode);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/users/{created.Id}",
            new
            {
                email = editedEmail,
                displayName = "Promoted Administrator",
                isEnabled = true,
                roles = new[] { RoleNames.Administrator },
                version = created.Version,
            });
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<UserResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal(editedEmail, updated.Email);
        Assert.Equal("Promoted Administrator", updated.DisplayName);
        Assert.Equal([RoleNames.Administrator], updated.Roles);
        Assert.NotEqual(created.Version, updated.Version);

        var staleResponse = await client.PutAsJsonAsync(
            $"/api/v1/users/{created.Id}",
            new
            {
                email = editedEmail,
                displayName = "Stale Update",
                isEnabled = true,
                roles = new[] { RoleNames.Administrator },
                version = created.Version,
            });
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        var staleProblem = await staleResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.EndsWith(
            "/concurrency-conflict",
            staleProblem.GetProperty("type").GetString(),
            StringComparison.Ordinal);

        var archiveResponse = await client.DeleteAsync(
            $"/api/v1/users/{created.Id}?version={updated.Version}");
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse.StatusCode);

        var archivedResponse = await client.GetAsync($"/api/v1/users/{created.Id}");
        archivedResponse.EnsureSuccessStatusCode();
        var archived = await archivedResponse.Content.ReadFromJsonAsync<UserResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(archived);
        Assert.False(archived.IsEnabled);
        Assert.NotNull(archived.ArchivedAtUtc);
        Assert.NotEqual(updated.Version, archived.Version);

        using var archivedLoginClient = factory.CreateHttpsClient();
        var loginCsrf = await ApiTestClient.GetCsrfTokenAsync(archivedLoginClient);
        archivedLoginClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", loginCsrf);
        var archivedLogin = await archivedLoginClient.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = editedEmail,
                password = temporaryPassword,
                rememberMe = false,
            });
        Assert.Equal(HttpStatusCode.Unauthorized, archivedLogin.StatusCode);

        var restoreResponse = await client.PostAsJsonAsync(
            $"/api/v1/users/{created.Id}/restore",
            new { version = archived.Version });
        restoreResponse.EnsureSuccessStatusCode();
        var restored = await restoreResponse.Content.ReadFromJsonAsync<UserResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(restored);
        Assert.True(restored.IsEnabled);
        Assert.Null(restored.ArchivedAtUtc);
        Assert.Equal([RoleNames.Administrator], restored.Roles);
        Assert.NotEqual(archived.Version, restored.Version);

        using var restoredClient = await factory.CreateAuthenticatedClientAsync(
            editedEmail,
            temporaryPassword);

        var auditActions = await factory.GetUserAuditActionsAsync(created.Id);
        Assert.Contains("user.created", auditActions);
        Assert.Contains("user.updated", auditActions);
        Assert.Contains("user.archived", auditActions);
        Assert.Contains("user.restored", auditActions);
    }

    [Fact]
    public async Task FinalAdministratorCannotRemoveOrArchiveOwnAccess()
    {
        using var factory = new InventoryApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var listResponse = await client.GetAsync("/api/v1/users?page=1&pageSize=200");
        listResponse.EnsureSuccessStatusCode();
        var users = await listResponse.Content
            .ReadFromJsonAsync<PagedResponse<UserResponse>>(ApiTestClient.JsonOptions);
        var administrator = Assert.Single(
            users!.Items,
            user => user.Email == InventoryApiFactory.AdministratorEmail);

        var removeRoleResponse = await client.PutAsJsonAsync(
            $"/api/v1/users/{administrator.Id}",
            new
            {
                email = administrator.Email,
                displayName = administrator.DisplayName,
                isEnabled = true,
                roles = new[] { RoleNames.Technician },
                version = administrator.Version,
            });
        Assert.Equal(HttpStatusCode.Conflict, removeRoleResponse.StatusCode);

        var archiveResponse = await client.DeleteAsync(
            $"/api/v1/users/{administrator.Id}?version={administrator.Version}");
        Assert.Equal(HttpStatusCode.Conflict, archiveResponse.StatusCode);
    }

    [Fact]
    public async Task MustChangePasswordBlocksPermissionsUntilThePasswordIsChanged()
    {
        using var factory = new InventoryApiFactory();
        using var admin = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);

        var unique = Guid.NewGuid().ToString("N");
        var email = $"gated-{unique}@inventory.test";
        const string temporaryPassword = "Temporary-Gate-Password!42";
        const string permanentPassword = "Permanent-Gate-Password!42";
        var createResponse = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new
            {
                email,
                displayName = "Gated Technician",
                temporaryPassword,
                roles = new[] { RoleNames.Technician },
            });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var gated = await factory.CreateAuthenticatedClientAsync(
            email,
            temporaryPassword);
        var blocked = await gated.GetAsync("/api/v1/assets");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        var me = await gated.GetFromJsonAsync<CurrentUserResponse>(
            "/api/v1/auth/me",
            ApiTestClient.JsonOptions);
        Assert.NotNull(me);
        Assert.True(me.MustChangePassword);

        var changeResponse = await gated.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new
            {
                currentPassword = temporaryPassword,
                newPassword = permanentPassword,
            });
        Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);

        var allowed = await gated.GetAsync("/api/v1/assets");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task NonAdministratorCannotUseAccountAdministration()
    {
        using var factory = new InventoryApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RoleSeedMigratesLegacyInventoryOperatorsToTechnicians()
    {
        using var factory = new InventoryApiFactory(seedDefaultUsers: false);
        _ = await factory.GetDatabaseStateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var roleManager =
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var seeder =
            scope.ServiceProvider.GetRequiredService<IRolePermissionSeeder>();

        var legacyRole = new IdentityRole<Guid>(RoleNames.LegacyInventoryOperator);
        Assert.True((await roleManager.CreateAsync(legacyRole)).Succeeded);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "legacy-operator@inventory.test",
            Email = "legacy-operator@inventory.test",
            EmailConfirmed = true,
            DisplayName = "Legacy Operator",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Version = Guid.NewGuid(),
        };
        Assert.True(
            (await userManager.CreateAsync(
                user,
                "Legacy-Operator-Password!42")).Succeeded);
        Assert.True(
            (await userManager.AddToRoleAsync(
                user,
                RoleNames.LegacyInventoryOperator)).Succeeded);

        await seeder.SeedAsync(CancellationToken.None);

        var roles = await userManager.GetRolesAsync(user);
        Assert.Contains(RoleNames.Technician, roles);
        Assert.DoesNotContain(RoleNames.LegacyInventoryOperator, roles);
        Assert.Null(
            await roleManager.FindByNameAsync(RoleNames.LegacyInventoryOperator));
    }
}
