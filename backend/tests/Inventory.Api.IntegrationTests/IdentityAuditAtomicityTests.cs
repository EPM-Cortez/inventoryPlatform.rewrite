using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Security;
using Inventory.Contracts.V1;

namespace Inventory.Api.IntegrationTests;

public sealed class IdentityAuditAtomicityTests
{
    private const string CreateAuditFailureTrigger = """
        CREATE TRIGGER TR_IdentityAuditFailure
        BEFORE INSERT ON AuditLogs
        WHEN NEW.Action = '{0}'
        BEGIN
            SELECT RAISE(ABORT, 'forced_identity_audit_failure');
        END;
        """;

    [Fact]
    public async Task LoginDoesNotIssueASessionWhenItsAuditCannotBePersisted()
    {
        using var factory = new InventoryApiFactory();
        using var client = factory.CreateHttpsClient();
        var before = await factory.CountAuditActionsAsync("authentication.login");
        await CreateFailureTriggerAsync(factory, "authentication.login");

        var csrf = await ApiTestClient.GetCsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = InventoryApiFactory.AdministratorEmail,
                password = InventoryApiFactory.AdministratorPassword,
                rememberMe = false,
            });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await DropFailureTriggerAsync(factory);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(
            before,
            await factory.CountAuditActionsAsync("authentication.login"));
    }

    [Fact]
    public async Task PasswordAndLogoutRemainUnchangedWhenTheirAuditCannotBePersisted()
    {
        using var factory = new InventoryApiFactory();
        using var client = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        const string newPassword = "Replacement-Administrator-Password!42";

        await CreateFailureTriggerAsync(factory, "user.password.changed");
        var passwordResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new
            {
                currentPassword = InventoryApiFactory.AdministratorPassword,
                newPassword,
            });
        Assert.Equal(HttpStatusCode.InternalServerError, passwordResponse.StatusCode);
        await DropFailureTriggerAsync(factory);

        using var oldPasswordClient = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var rejectedNewPasswordClient = factory.CreateHttpsClient();
        var csrf = await ApiTestClient.GetCsrfTokenAsync(rejectedNewPasswordClient);
        rejectedNewPasswordClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        var newPasswordLogin = await rejectedNewPasswordClient.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = InventoryApiFactory.AdministratorEmail,
                password = newPassword,
                rememberMe = false,
            });
        Assert.Equal(HttpStatusCode.Unauthorized, newPasswordLogin.StatusCode);

        await CreateFailureTriggerAsync(factory, "authentication.logout");
        var logoutResponse = await oldPasswordClient.PostAsync(
            "/api/v1/auth/logout",
            content: null);
        Assert.Equal(HttpStatusCode.InternalServerError, logoutResponse.StatusCode);
        await DropFailureTriggerAsync(factory);
        Assert.Equal(
            HttpStatusCode.OK,
            (await oldPasswordClient.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task UserCreationAndRoleChangesRollBackWhenTheirAuditCannotBePersisted()
    {
        using var factory = new InventoryApiFactory();
        using var administrator =
            await factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"atomic-{suffix}@inventory.test";
        const string password = "Atomic-Identity-Test-Password!42";

        await CreateFailureTriggerAsync(factory, "user.created");
        var rejectedCreate = await administrator.PostAsJsonAsync(
            "/api/v1/users",
            new CreateUserRequest
            {
                Email = email,
                DisplayName = "Atomic Identity User",
                TemporaryPassword = password,
                Roles = [RoleNames.Technician],
            },
            ApiTestClient.JsonOptions);
        Assert.Equal(
            HttpStatusCode.InternalServerError,
            rejectedCreate.StatusCode);
        await DropFailureTriggerAsync(factory);

        using var rejectedLogin = factory.CreateHttpsClient();
        var csrf = await ApiTestClient.GetCsrfTokenAsync(rejectedLogin);
        rejectedLogin.DefaultRequestHeaders.Add(
            "X-CSRF-TOKEN",
            csrf);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await rejectedLogin.PostAsJsonAsync(
                "/api/v1/auth/login",
                new
                {
                    email,
                    password,
                    rememberMe = false,
                })).StatusCode);

        var create = await administrator.PostAsJsonAsync(
            "/api/v1/users",
            new CreateUserRequest
            {
                Email = email,
                DisplayName = "Atomic Identity User",
                TemporaryPassword = password,
                Roles = [RoleNames.Technician],
            },
            ApiTestClient.JsonOptions);
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<UserResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(created);

        await CreateFailureTriggerAsync(
            factory,
            "user.roles.changed");
        var rejectedRoleChange =
            await administrator.PutAsJsonAsync(
                $"/api/v1/users/{created.Id}/roles",
                new SetUserRolesRequest
                {
                    Roles = [RoleNames.ReadOnlyAuditor],
                    Version = created.Version,
                },
                ApiTestClient.JsonOptions);
        Assert.Equal(
            HttpStatusCode.InternalServerError,
            rejectedRoleChange.StatusCode);
        await DropFailureTriggerAsync(factory);

        var unchanged = await administrator.GetFromJsonAsync<UserResponse>(
            $"/api/v1/users/{created.Id}",
            ApiTestClient.JsonOptions);
        Assert.NotNull(unchanged);
        Assert.Equal(created.Version, unchanged.Version);
        Assert.Equal([RoleNames.Technician], unchanged.Roles);
        Assert.Equal(
            0,
            await factory.CountAuditActionsAsync(
                "user.roles.changed"));
    }

    private static Task CreateFailureTriggerAsync(
        InventoryApiFactory factory,
        string action) =>
        factory.ExecuteSqlAsync(
            CreateAuditFailureTrigger.Replace(
                "{0}",
                action,
                StringComparison.Ordinal));

    private static Task DropFailureTriggerAsync(InventoryApiFactory factory) =>
        factory.ExecuteSqlAsync("DROP TRIGGER TR_IdentityAuditFailure;");
}
