using Inventory.Application.Common;
using Inventory.Application.Identity;
using Inventory.Application.Security;
using Inventory.Domain.Auditing;
using Inventory.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Identity;

public sealed class AdminBootstrapper : IAdminBootstrapper
{
    private readonly IRolePermissionSeeder _rolePermissionSeeder;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly InventoryDbContext _dbContext;
    private readonly IClock _clock;

    public AdminBootstrapper(
        IRolePermissionSeeder rolePermissionSeeder,
        UserManager<ApplicationUser> userManager,
        InventoryDbContext dbContext,
        IClock clock)
    {
        _rolePermissionSeeder = rolePermissionSeeder;
        _userManager = userManager;
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task BootstrapAsync(
        BootstrapAdminCommand command,
        CancellationToken cancellationToken)
    {
        await _rolePermissionSeeder.SeedAsync(cancellationToken);

        var email = command.Email.Trim();
        var existing = await _userManager.FindByEmailAsync(email);
        var administrators = await _userManager.GetUsersInRoleAsync(RoleNames.Administrator);
        var enabledAdministratorExists = administrators.Any(
            user => user.IsEnabled && !user.ArchivedAtUtc.HasValue);
        var existingIsAdministrator =
            existing is not null &&
            await _userManager.IsInRoleAsync(existing, RoleNames.Administrator);

        if (enabledAdministratorExists && !existingIsAdministrator)
        {
            throw new ResourceConflictException(
                "administrator_already_exists",
                "An enabled administrator already exists. Use the authenticated user administration API.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (existing is null)
        {
            existing = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                DisplayName = command.DisplayName.Trim(),
                EmailConfirmed = true,
                IsEnabled = true,
                MustChangePassword = false,
                CreatedAtUtc = _clock.UtcNow,
                Version = Guid.NewGuid(),
            };

            ThrowIfFailed(
                await _userManager.CreateAsync(existing, command.Password),
                "create the bootstrap administrator");
        }
        else
        {
            if (!existing.IsEnabled)
            {
                throw new ResourceConflictException(
                    "administrator_disabled",
                    "The requested bootstrap account exists but is disabled.");
            }
        }

        if (!existingIsAdministrator)
        {
            ThrowIfFailed(
                await _userManager.AddToRoleAsync(existing, RoleNames.Administrator),
                "grant the Administrator role");
            existing.Version = Guid.NewGuid();
            ThrowIfFailed(
                await _userManager.UpdateAsync(existing),
                "version the bootstrap administrator");
        }

        _dbContext.AuditLogs.Add(
            AuditLog.Create(
                existing.Id,
                existing.DisplayName,
                "administrator.bootstrap",
                "user",
                existing.Id.ToString(),
                _clock.UtcNow,
                "succeeded"));

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void ThrowIfFailed(IdentityResult result, string action)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw IdentityFailure(action, result);
    }

    private static RequestValidationException IdentityFailure(
        string action,
        IdentityResult result)
    {
        var errors = result.Errors
            .GroupBy(error => error.Code, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray(),
                StringComparer.Ordinal);

        return new RequestValidationException(
            "identity_operation_failed",
            $"Unable to {action}.",
            errors);
    }
}
