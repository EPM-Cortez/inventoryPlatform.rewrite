using System.Security.Claims;
using Inventory.Application.Security;
using Inventory.Infrastructure.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Identity;

public sealed class ApplicationUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>
{
    private readonly MfaSecurityOptions _mfaOptions;

    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOptions<IdentityOptions> optionsAccessor,
        IOptions<MfaSecurityOptions> mfaOptions)
        : base(userManager, roleManager, optionsAccessor)
    {
        _mfaOptions = mfaOptions.Value;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim("display_name", user.DisplayName));
        identity.AddClaim(
            new Claim(
                Permissions.MustChangePasswordClaimType,
                user.MustChangePassword ? bool.TrueString : bool.FalseString,
                ClaimValueTypes.Boolean));
        var roles = await UserManager.GetRolesAsync(user);
        var enrollmentRequired =
            _mfaOptions.EnforceRequiredRoles &&
            !user.TwoFactorEnabled &&
            roles.Any(
                role => _mfaOptions.RequiredRoles.Contains(
                    role,
                    StringComparer.Ordinal));
        identity.AddClaim(
            new Claim(
                Permissions.MfaEnrollmentRequiredClaimType,
                enrollmentRequired ? bool.TrueString : bool.FalseString,
                ClaimValueTypes.Boolean));
        return identity;
    }
}
