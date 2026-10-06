using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Inventory.Api.Security;

public sealed class ApiAntiforgeryFilter : IAsyncAuthorizationFilter, IOrderedFilter
{
    private static readonly HashSet<string> SafeMethods =
        new(
            [HttpMethods.Get, HttpMethods.Head, HttpMethods.Options, HttpMethods.Trace],
            StringComparer.OrdinalIgnoreCase);

    private readonly IAntiforgery _antiforgery;

    public ApiAntiforgeryFilter(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    public int Order => -1000;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var antiforgeryIgnored =
            context.Filters.OfType<IgnoreAntiforgeryTokenAttribute>().Any() ||
            context.HttpContext.GetEndpoint()?.Metadata
                .GetMetadata<IgnoreAntiforgeryTokenAttribute>() is not null;
        if (!antiforgeryIgnored &&
            !SafeMethods.Contains(context.HttpContext.Request.Method))
        {
            await _antiforgery.ValidateRequestAsync(context.HttpContext);
        }
    }
}
