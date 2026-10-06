using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Inventory.Application.Identity;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.IntegrationTests;

public sealed class RolePresetSessionReconciliationTests
{
    [Fact]
    public async Task ReconciledRolePermissionReachesAnExistingCookieOnItsNextRequest()
    {
        using var factory = new InventoryApiFactory();
        using var administrator = await factory.CreateAuthenticatedClientAsync(
            InventoryApiFactory.AdministratorEmail,
            InventoryApiFactory.AdministratorPassword);

        Assert.Equal(
            HttpStatusCode.OK,
            (await administrator.GetAsync("/api/v1/agents")).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roleManager =
                scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var role = await roleManager.FindByNameAsync(RoleNames.Administrator)
                ?? throw new InvalidOperationException("The Administrator role was not seeded.");
            var persistedClaim = Assert.Single(
                await roleManager.GetClaimsAsync(role),
                claim =>
                    claim.Type == Permissions.ClaimType &&
                    claim.Value == Permissions.AgentsRead);
            var result = await roleManager.RemoveClaimAsync(role, persistedClaim);
            Assert.True(result.Succeeded);
        }

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await administrator.GetAsync("/api/v1/agents")).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<IRolePermissionSeeder>();
            await seeder.SeedAsync(CancellationToken.None);
        }

        Assert.Equal(
            HttpStatusCode.OK,
            (await administrator.GetAsync("/api/v1/agents")).StatusCode);
        var currentUser = await administrator.GetFromJsonAsync<CurrentUserResponse>(
            "/api/v1/auth/me",
            ApiTestClient.JsonOptions);
        Assert.Contains(Permissions.AgentsRead, currentUser!.Permissions);
    }

    [Fact]
    public async Task RoleClaimRepairRollsBackWhenItsAuditCannotBePersisted()
    {
        using var factory = new InventoryApiFactory();
        _ = await factory.GetDatabaseStateAsync();
        var auditCountBefore = await factory.CountAuditActionsAsync(
            "identity.role_presets.reconciled");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roleManager =
                scope.ServiceProvider.GetRequiredService<
                    RoleManager<IdentityRole<Guid>>>();
            var role = await roleManager.FindByNameAsync(RoleNames.Administrator)
                ?? throw new InvalidOperationException(
                    "The Administrator role was not seeded.");
            var persistedClaim = Assert.Single(
                await roleManager.GetClaimsAsync(role),
                claim =>
                    claim.Type == Permissions.ClaimType &&
                    claim.Value == Permissions.AgentsManage);
            Assert.True(
                (await roleManager.RemoveClaimAsync(
                    role,
                    persistedClaim)).Succeeded);
        }

        await factory.ExecuteSqlAsync(
            """
            CREATE TRIGGER TR_RolePresetAuditFailure
            BEFORE INSERT ON AuditLogs
            WHEN NEW.Action = 'identity.role_presets.reconciled'
            BEGIN
                SELECT RAISE(ABORT, 'forced_role_preset_audit_failure');
            END;
            """);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var seeder =
                scope.ServiceProvider.GetRequiredService<IRolePermissionSeeder>();
            await Assert.ThrowsAsync<DbUpdateException>(
                () => seeder.SeedAsync(CancellationToken.None));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roleManager =
                scope.ServiceProvider.GetRequiredService<
                    RoleManager<IdentityRole<Guid>>>();
            var role = await roleManager.FindByNameAsync(RoleNames.Administrator)
                ?? throw new InvalidOperationException(
                    "The Administrator role was not seeded.");
            Assert.DoesNotContain(
                await roleManager.GetClaimsAsync(role),
                claim =>
                    claim.Type == Permissions.ClaimType &&
                    claim.Value == Permissions.AgentsManage);
        }

        Assert.Equal(
            auditCountBefore,
            await factory.CountAuditActionsAsync(
                "identity.role_presets.reconciled"));

        await factory.ExecuteSqlAsync(
            "DROP TRIGGER TR_RolePresetAuditFailure;");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var seeder =
                scope.ServiceProvider.GetRequiredService<IRolePermissionSeeder>();
            await seeder.SeedAsync(CancellationToken.None);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roleManager =
                scope.ServiceProvider.GetRequiredService<
                    RoleManager<IdentityRole<Guid>>>();
            var role = await roleManager.FindByNameAsync(RoleNames.Administrator)
                ?? throw new InvalidOperationException(
                    "The Administrator role was not seeded.");
            Assert.Contains(
                await roleManager.GetClaimsAsync(role),
                claim =>
                    claim.Type == Permissions.ClaimType &&
                    claim.Value == Permissions.AgentsManage);
        }

        Assert.Equal(
            auditCountBefore + 1,
            await factory.CountAuditActionsAsync(
                "identity.role_presets.reconciled"));
    }

    [Fact]
    public async Task NormalStartupReconcilesPersistedRoleDriftBeforeServing()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "inventory-role-startup-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var databasePath = Path.Combine(testDirectory, "inventory.db");
        var connectionString =
            $"Data Source={databasePath};Foreign Keys=True;Default Timeout=30";

        try
        {
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlite(connectionString)
                .Options;
            await using (var dbContext = new InventoryDbContext(options))
            {
                await dbContext.Database.MigrateAsync();
                var administratorRole = new IdentityRole<Guid>(
                    RoleNames.Administrator)
                {
                    Id = Guid.NewGuid(),
                    NormalizedName =
                        RoleNames.Administrator.ToUpperInvariant(),
                    ConcurrencyStamp = Guid.NewGuid().ToString(),
                };
                dbContext.Roles.Add(administratorRole);
                var excludedPermissions = new HashSet<string>(
                    [
                        Permissions.AgentsRead,
                        Permissions.AgentsManage,
                        Permissions.AgentsRecover,
                        Permissions.AuditExport,
                        Permissions.LegacyReconcile,
                    ],
                    StringComparer.Ordinal);
                foreach (var permission in Permissions.All.Except(
                             excludedPermissions,
                             StringComparer.Ordinal))
                {
                    dbContext.RoleClaims.Add(
                        new IdentityRoleClaim<Guid>
                        {
                            RoleId = administratorRole.Id,
                            ClaimType = Permissions.ClaimType,
                            ClaimValue = permission,
                        });
                }

                await dbContext.SaveChangesAsync();
                Assert.Equal(
                    Permissions.All.Count - excludedPermissions.Count,
                    await dbContext.RoleClaims.CountAsync(
                        claim =>
                            claim.RoleId == administratorRole.Id &&
                            claim.ClaimType == Permissions.ClaimType));
            }

            using (var firstFactory = CreateFactory(connectionString))
            {
                _ = firstFactory.Services;
                await using var scope =
                    firstFactory.Services.CreateAsyncScope();
                var dbContext =
                    scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                var normalizedAdministratorRole =
                    RoleNames.Administrator.ToUpperInvariant();
                var persistedPermissions = await dbContext.RoleClaims
                    .Where(
                        claim =>
                            claim.ClaimType == Permissions.ClaimType &&
                            dbContext.Roles.Any(
                                role =>
                                    role.Id == claim.RoleId &&
                                    role.NormalizedName ==
                                    normalizedAdministratorRole))
                    .Select(claim => claim.ClaimValue!)
                    .OrderBy(permission => permission)
                    .ToArrayAsync();
                Assert.Equal(
                    Permissions.All.Order(StringComparer.Ordinal),
                    persistedPermissions);
                Assert.Equal(
                    1,
                    await dbContext.AuditLogs.CountAsync(
                        audit =>
                            audit.Action ==
                            "identity.role_presets.reconciled"));
            }

            using (var secondFactory = CreateFactory(connectionString))
            {
                _ = secondFactory.Services;
                await using var scope =
                    secondFactory.Services.CreateAsyncScope();
                var dbContext =
                    scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                Assert.Equal(
                    1,
                    await dbContext.AuditLogs.CountAsync(
                        audit =>
                            audit.Action ==
                            "identity.role_presets.reconciled"));
            }
        }
        finally
        {
            try
            {
                Directory.Delete(testDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting(
                "ConnectionStrings:InventoryDatabase",
                connectionString);
        });
}
