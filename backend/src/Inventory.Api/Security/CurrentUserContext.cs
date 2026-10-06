using System.Security.Claims;
using Inventory.Application.Common;
using Inventory.Application.Security;

namespace Inventory.Api.Security;

public sealed class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Roles =>
        GetClaims(ClaimTypes.Role);

    public IReadOnlyCollection<string> Permissions =>
        GetClaims(Inventory.Application.Security.Permissions.ClaimType);

    public CurrentActor RequireActor()
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP request is available.");
        var principal = httpContext.User;

        if (principal.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(
                principal.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId))
        {
            throw new InvalidOperationException("An authenticated user is required.");
        }

        var displayName =
            principal.FindFirstValue("display_name")
            ?? principal.Identity.Name
            ?? userId.ToString();

        return new CurrentActor(
            userId,
            displayName,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString(),
            httpContext.TraceIdentifier);
    }

    private string[] GetClaims(string claimType) =>
        _httpContextAccessor.HttpContext?.User
            .FindAll(claimType)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray()
        ?? [];
}
