using Microsoft.AspNetCore.Identity;
using Inventory.Infrastructure.Identity;

namespace Inventory.Api.Security;

public sealed class EnabledUserValidationMiddleware
{
    private readonly RequestDelegate _next;

    public EnabledUserValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var user = await userManager.GetUserAsync(context.User);
            if (user is null || !user.IsEnabled || user.ArchivedAtUtc.HasValue)
            {
                await signInManager.SignOutAsync();
                context.User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity());
            }
        }

        await _next(context);
    }
}
