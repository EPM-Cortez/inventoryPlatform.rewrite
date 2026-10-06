using System.Security.Claims;
using Inventory.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Security;

public sealed class PendingMfaCookieEvents : CookieAuthenticationEvents
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IOptions<IdentityOptions> _identityOptions;
    private readonly PendingMfaChallengeStore _challenges;

    public PendingMfaCookieEvents(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IOptions<IdentityOptions> identityOptions,
        PendingMfaChallengeStore challenges)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _identityOptions = identityOptions;
        _challenges = challenges;
    }

    public override async Task SigningIn(CookieSigningInContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity ||
            !Guid.TryParse(
                context.Principal.FindFirstValue(ClaimTypes.Name),
                out var userId))
        {
            throw new InvalidOperationException(
                "The pending MFA ticket has no valid user identity.");
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsEnabled || user.ArchivedAtUtc.HasValue)
        {
            throw new InvalidOperationException(
                "The pending MFA ticket is not for an enabled account.");
        }

        var stamp = await _userManager.GetSecurityStampAsync(user);
        if (string.IsNullOrWhiteSpace(stamp))
        {
            throw new InvalidOperationException(
                "The pending MFA ticket cannot be bound to a security stamp.");
        }

        identity.AddClaim(
            new Claim(
                _identityOptions.Value.ClaimsIdentity.SecurityStampClaimType,
                stamp));
        var challengeToken = await _challenges.CreateAsync(
            userId,
            stamp,
            context.HttpContext.RequestAborted);
        identity.AddClaim(new Claim(
            PendingMfaChallengeStore.CookieClaimType,
            challengeToken));
    }

    public override async Task ValidatePrincipal(
        CookieValidatePrincipalContext context)
    {
        var user = context.Principal is null
            ? null
            : await _signInManager.ValidateTwoFactorSecurityStampAsync(
                context.Principal);
        var token = context.Principal?.FindFirstValue(
            PendingMfaChallengeStore.CookieClaimType);
        var stamp = context.Principal?.FindFirstValue(
            _identityOptions.Value.ClaimsIdentity.SecurityStampClaimType);
        if (user is not null && user.IsEnabled && !user.ArchivedAtUtc.HasValue &&
            !string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(stamp) &&
            await _challenges.IsActiveAsync(
                token,
                user.Id,
                stamp,
                context.HttpContext.RequestAborted))
        {
            return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(
            IdentityConstants.TwoFactorUserIdScheme);
    }
}
