using System.Net;
using System.Net.Http.Json;

namespace Inventory.Api.IntegrationTests;

public sealed class PasswordOperationRateLimitTests
{
    [Fact]
    public async Task SelfServicePasswordChangesUseDedicatedRateLimit()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            passwordOperationPermitLimit: 2);
        using var client = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);

        async Task<HttpResponseMessage> ChangeWithWrongCurrentPassword() =>
            await client.PostAsJsonAsync(
                "/api/v1/auth/change-password",
                new
                {
                    currentPassword = "Wrong-Current-Password!42",
                    newPassword = "Replacement-Password!42",
                });

        using var first = await ChangeWithWrongCurrentPassword();
        using var second = await ChangeWithWrongCurrentPassword();
        using var third = await ChangeWithWrongCurrentPassword();

        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task AdministratorPasswordResetsUseDedicatedRateLimit()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            passwordOperationPermitLimit: 2);
        using var client = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        var missingUserId = Guid.NewGuid();

        async Task<HttpResponseMessage> ResetMissingUserPassword() =>
            await client.PostAsJsonAsync(
                $"/api/v1/users/{missingUserId}/reset-password",
                new
                {
                    newTemporaryPassword = "Replacement-Password!42",
                    version = Guid.NewGuid(),
                });

        using var first = await ResetMissingUserPassword();
        using var second = await ResetMissingUserPassword();
        using var third = await ResetMissingUserPassword();

        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task PasswordOperationLimitsPreserveAuthenticationAuthorizationAndAntiforgery()
    {
        using var factory = new InventoryApiFactory();
        using var anonymous = factory.CreateHttpsClient();
        var anonymousCsrf = await ApiTestClient.GetCsrfTokenAsync(anonymous);
        anonymous.DefaultRequestHeaders.Add("X-CSRF-TOKEN", anonymousCsrf);
        using var anonymousChange = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new
            {
                currentPassword = "Current-Password!42",
                newPassword = "Replacement-Password!42",
            });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousChange.StatusCode);

        using var technician = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        using var forbiddenReset = await technician.PostAsJsonAsync(
            $"/api/v1/users/{Guid.NewGuid()}/reset-password",
            new
            {
                newTemporaryPassword = "Replacement-Password!42",
                version = Guid.NewGuid(),
            });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenReset.StatusCode);

        using var administrator = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        administrator.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var missingAntiforgery = await administrator.PostAsJsonAsync(
            $"/api/v1/users/{Guid.NewGuid()}/reset-password",
            new
            {
                newTemporaryPassword = "Replacement-Password!42",
                version = Guid.NewGuid(),
            });
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgery.StatusCode);
    }
}
