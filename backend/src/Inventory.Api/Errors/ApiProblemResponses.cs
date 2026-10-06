using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Errors;

internal static class ApiProblemResponses
{
    public static async ValueTask WriteAsync(
        HttpContext httpContext,
        int status,
        string type,
        string title,
        string detail,
        CancellationToken cancellationToken = default)
    {
        httpContext.Response.Headers.Remove("Location");
        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Type = type,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: (JsonSerializerOptions?)null,
            contentType: "application/problem+json",
            cancellationToken);
    }
}
