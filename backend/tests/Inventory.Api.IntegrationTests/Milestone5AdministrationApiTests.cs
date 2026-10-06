using System.Net;
using System.Net.Http.Json;
using System.Text;
using Inventory.Application.Security;
using Inventory.Contracts.V1;

namespace Inventory.Api.IntegrationTests;

public sealed class Milestone5AdministrationApiTests
{
    [Fact]
    public async Task SchoolSettingsAreSingleVersionedValidatedAndAudited()
    {
        using var factory = new InventoryApiFactory();
        using var administrator = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);
        using var technician = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await technician.GetAsync("/api/v1/settings/school")).StatusCode);

        var initial = await administrator.GetFromJsonAsync<SchoolSettingsResponse>(
            "/api/v1/settings/school",
            ApiTestClient.JsonOptions);
        Assert.NotNull(initial);
        Assert.False(initial.IsPersisted);
        Assert.Null(initial.Version);

        var create = await administrator.PutAsJsonAsync(
            "/api/v1/settings/school",
            new
            {
                name = "South Thames School",
                code = "STS",
                timeZone = "Europe/London",
                qrBaseUrl = "https://inventory.example.school/scan",
                warrantyWarningDays = 45,
                version = (Guid?)null,
            });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<SchoolSettingsResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(created);
        Assert.True(created.IsPersisted);
        Assert.NotNull(created.Version);
        Assert.Equal(45, created.WarrantyWarningDays);

        var update = await administrator.PutAsJsonAsync(
            "/api/v1/settings/school",
            new
            {
                name = "South Thames School",
                code = "STS",
                timeZone = "Europe/London",
                qrBaseUrl = "https://inventory.example.school/assets/scan",
                warrantyWarningDays = 60,
                version = created.Version,
            });
        update.EnsureSuccessStatusCode();
        var updated = await update.Content.ReadFromJsonAsync<SchoolSettingsResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(updated);
        Assert.NotEqual(created.Version, updated.Version);

        var stale = await administrator.PutAsJsonAsync(
            "/api/v1/settings/school",
            new
            {
                name = "Stale School",
                code = "STALE",
                timeZone = "Europe/London",
                qrBaseUrl = "https://inventory.example.school/scan",
                warrantyWarningDays = 60,
                version = created.Version,
            });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var invalidTimeZone = await administrator.PutAsJsonAsync(
            "/api/v1/settings/school",
            new
            {
                name = updated.Name,
                code = updated.Code,
                timeZone = "Not/A-Time-Zone",
                qrBaseUrl = updated.QrBaseUrl,
                warrantyWarningDays = updated.WarrantyWarningDays,
                version = updated.Version,
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidTimeZone.StatusCode);
        Assert.Equal(1, await factory.CountAuditActionsAsync("school.settings.created"));
        Assert.Equal(1, await factory.CountAuditActionsAsync("school.settings.updated"));
    }

    [Fact]
    public async Task AuditExportIsPermissionedBoundedFormulaSafeAndAudited()
    {
        using var factory = new InventoryApiFactory(
            seedDefaultUsers: true,
            auditMaxExportRows: 2);
        using var administrator = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);

        var unique = Guid.NewGuid().ToString("N");
        var maliciousEmail = $"audit-formula-{unique}@inventory.test";
        const string maliciousPassword = "Audit-Formula-Password!42";
        var create = await administrator.PostAsJsonAsync(
            "/api/v1/users",
            new
            {
                email = maliciousEmail,
                displayName = "=HYPERLINK(\"https://invalid.example\")",
                temporaryPassword = maliciousPassword,
                roles = new[] { RoleNames.Technician },
            });
        create.EnsureSuccessStatusCode();
        var maliciousUser = await create.Content.ReadFromJsonAsync<UserResponse>(
            ApiTestClient.JsonOptions);
        Assert.NotNull(maliciousUser);

        using var maliciousLogin = await factory.CreateAuthenticatedClientAsync(
            maliciousEmail,
            maliciousPassword);
        using var secondLogin = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        using var auditor = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AuditorEmail,
            InventoryApiFactory.AuditorPassword);

        var export = await auditor.GetAsync(
            "/api/v1/audit/export?action=authentication.login");
        export.EnsureSuccessStatusCode();
        Assert.Equal("2", Assert.Single(export.Headers.GetValues("X-Inventory-Row-Count")));
        Assert.Equal("true", Assert.Single(export.Headers.GetValues("X-Inventory-Truncated")));
        Assert.True(export.Headers.CacheControl?.NoStore);
        var csv = Encoding.UTF8.GetString(await export.Content.ReadAsByteArrayAsync());
        Assert.StartsWith(
            "\uFEFFid,occurredAtUtc,actorUserId,actorDisplayName",
            csv,
            StringComparison.Ordinal);
        Assert.DoesNotContain("detailsJson", csv, StringComparison.Ordinal);

        var formulaExport = await auditor.GetAsync(
            "/api/v1/audit/export?action=authentication.login&targetId=" +
            Uri.EscapeDataString(maliciousUser.Id.ToString()));
        formulaExport.EnsureSuccessStatusCode();
        var formulaCsv = Encoding.UTF8.GetString(
            await formulaExport.Content.ReadAsByteArrayAsync());
        Assert.Contains(
            "'=HYPERLINK(\"\"https://invalid.example\"\")",
            formulaCsv,
            StringComparison.Ordinal);
        Assert.Equal(2, await factory.CountAuditActionsAsync("audit.exported"));

        using var denied = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.TechnicianEmail,
            InventoryApiFactory.TechnicianPassword);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await denied.GetAsync("/api/v1/audit/export")).StatusCode);
    }
}
