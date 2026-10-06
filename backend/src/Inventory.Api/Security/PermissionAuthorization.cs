using Inventory.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace Inventory.Api.Security;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var mustChangePassword = context.User.FindFirst(
            Permissions.MustChangePasswordClaimType)?.Value;
        if (string.Equals(mustChangePassword, bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var mfaEnrollmentRequired = context.User.FindFirst(
            Permissions.MfaEnrollmentRequiredClaimType)?.Value;
        if (string.Equals(
                mfaEnrollmentRequired,
                bool.TrueString,
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (context.User.HasClaim(Permissions.ClaimType, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
