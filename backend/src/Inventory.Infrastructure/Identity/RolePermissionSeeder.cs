using System.Security.Claims;
using System.Text.Json;
using Inventory.Application.Common;
using Inventory.Application.Identity;
using Inventory.Application.Security;
using Inventory.Domain.Auditing;
using Inventory.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Inventory.Infrastructure.Identity;

public sealed class RolePermissionSeeder : IRolePermissionSeeder
{
    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly Action<ILogger, int, int, int, bool, Exception?>
        LogRolePresetRepair = LoggerMessage.Define<int, int, int, bool>(
            LogLevel.Information,
            new EventId(1200, nameof(LogRolePresetRepair)),
            "Reconciled {RoleCount} identity role presets: added {AddedPermissionCount} permission claims, removed {RemovedPermissionCount}, legacy role migrated: {LegacyRoleMigrated}.");

    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly InventoryDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<RolePermissionSeeder> _logger;

    public RolePermissionSeeder(
        RoleManager<IdentityRole<Guid>> roleManager,
        InventoryDbContext dbContext,
        IClock clock,
        ILogger<RolePermissionSeeder> logger)
    {
        _roleManager = roleManager;
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var legacyRoleMigrated =
            await MigrateLegacyInventoryOperatorAsync(cancellationToken);
        var changes = new List<RolePresetChange>();

        foreach (var (roleName, expectedPermissions) in RolePermissionPresets.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var role = await _roleManager.FindByNameAsync(roleName);
            var roleCreated = role is null;
            if (role is null)
            {
                role = new IdentityRole<Guid>(roleName);
                ThrowIfFailed(await _roleManager.CreateAsync(role), $"create role {roleName}");
            }

            var existingClaims = await _roleManager.GetClaimsAsync(role);
            var existingPermissions = existingClaims
                .Where(claim => claim.Type == Permissions.ClaimType)
                .ToArray();
            var removedPermissions = existingPermissions
                .Where(claim => !expectedPermissions.Contains(claim.Value))
                .Select(claim => claim.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            foreach (var claim in existingPermissions.Where(
                         claim => removedPermissions.Contains(
                             claim.Value,
                             StringComparer.Ordinal)))
            {
                ThrowIfFailed(
                    await _roleManager.RemoveClaimAsync(role, claim),
                    $"remove obsolete permission {claim.Value} from {roleName}");
            }

            var currentValues = existingPermissions
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal);
            var addedPermissions = expectedPermissions
                .Where(permission => !currentValues.Contains(permission))
                .Order(StringComparer.Ordinal)
                .ToArray();
            foreach (var permission in addedPermissions)
            {
                ThrowIfFailed(
                    await _roleManager.AddClaimAsync(
                        role,
                        new Claim(Permissions.ClaimType, permission)),
                    $"grant permission {permission} to {roleName}");
            }

            if (roleCreated ||
                addedPermissions.Length > 0 ||
                removedPermissions.Length > 0)
            {
                changes.Add(
                    new RolePresetChange(
                        roleName,
                        roleCreated,
                        addedPermissions,
                        removedPermissions));
            }
        }

        if (legacyRoleMigrated || changes.Count > 0)
        {
            _dbContext.AuditLogs.Add(
                AuditLog.Create(
                    actorUserId: null,
                    actorDisplayName: "System",
                    action: "identity.role_presets.reconciled",
                    targetType: "role_presets",
                    targetId: null,
                    occurredAtUtc: _clock.UtcNow,
                    outcome: "succeeded",
                    detailsJson: JsonSerializer.Serialize(
                        new
                        {
                            legacyRoleMigrated,
                            changes,
                        },
                        AuditJsonOptions)));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        if (legacyRoleMigrated || changes.Count > 0)
        {
            LogRolePresetRepair(
                _logger,
                changes.Count,
                changes.Sum(change => change.AddedPermissions.Count),
                changes.Sum(change => change.RemovedPermissions.Count),
                legacyRoleMigrated,
                null);
        }
    }

    private async Task<bool> MigrateLegacyInventoryOperatorAsync(
        CancellationToken cancellationToken)
    {
        var legacyRole = await _roleManager.FindByNameAsync(
            RoleNames.LegacyInventoryOperator);
        if (legacyRole is null)
        {
            return false;
        }

        var technicianRole = await _roleManager.FindByNameAsync(RoleNames.Technician);
        if (technicianRole is null)
        {
            legacyRole.Name = RoleNames.Technician;
            ThrowIfFailed(
                await _roleManager.UpdateAsync(legacyRole),
                $"rename role {RoleNames.LegacyInventoryOperator} to {RoleNames.Technician}");
            return true;
        }

        var technicianUserIds = await _dbContext.Set<IdentityUserRole<Guid>>()
            .Where(userRole => userRole.RoleId == technicianRole.Id)
            .Select(userRole => userRole.UserId)
            .ToHashSetAsync(cancellationToken);
        var legacyAssignments = await _dbContext.Set<IdentityUserRole<Guid>>()
            .Where(userRole => userRole.RoleId == legacyRole.Id)
            .ToArrayAsync(cancellationToken);

        foreach (var assignment in legacyAssignments.Where(
                     assignment => !technicianUserIds.Contains(assignment.UserId)))
        {
            _dbContext.Set<IdentityUserRole<Guid>>().Add(
                new IdentityUserRole<Guid>
                {
                    UserId = assignment.UserId,
                    RoleId = technicianRole.Id,
                });
        }

        _dbContext.Set<IdentityUserRole<Guid>>().RemoveRange(legacyAssignments);
        await _dbContext.SaveChangesAsync(cancellationToken);
        ThrowIfFailed(
            await _roleManager.DeleteAsync(legacyRole),
            $"remove merged role {RoleNames.LegacyInventoryOperator}");
        return true;
    }

    private static void ThrowIfFailed(IdentityResult result, string action)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join("; ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Failed to {action}: {errors}");
    }

    private sealed record RolePresetChange(
        string Role,
        bool Created,
        IReadOnlyList<string> AddedPermissions,
        IReadOnlyList<string> RemovedPermissions);
}
