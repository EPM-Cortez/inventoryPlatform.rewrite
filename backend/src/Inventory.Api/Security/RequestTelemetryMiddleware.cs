using System.Diagnostics;
using Inventory.Infrastructure.Observability;

namespace Inventory.Api.Security;

public sealed partial class RequestTelemetryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTelemetryMiddleware> _logger;

    public RequestTelemetryMiddleware(
        RequestDelegate next,
        ILogger<RequestTelemetryMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOperationalMetrics metrics)
    {
        var started = Stopwatch.GetTimestamp();
        context.Response.OnStarting(
            () =>
            {
                context.Response.Headers["X-Correlation-ID"] =
                    context.TraceIdentifier;
                return Task.CompletedTask;
            });

        await _next(context);

        var elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var requestPath = GetLogSafePath(context.Request.Path);
            LogRequestCompleted(
                _logger,
                context.Request.Method,
                requestPath,
                context.Response.StatusCode,
                elapsedMilliseconds,
                context.TraceIdentifier);
        }

        if (context.Response.StatusCode < StatusCodes.Status400BadRequest)
        {
            return;
        }

        if (context.Request.Path.StartsWithSegments(
                "/api/v1/imports",
                StringComparison.OrdinalIgnoreCase))
        {
            metrics.RecordImportRejection();
        }

        if (context.Request.Path.StartsWithSegments(
                "/api/v1/qr",
                StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.Value?.Contains(
                "/qr",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            metrics.RecordQrFailure();
        }
    }

    private static string GetLogSafePath(PathString path)
    {
        if (path.StartsWithSegments(
                "/api/v1/qr",
                StringComparison.OrdinalIgnoreCase,
                out var remaining) &&
            remaining.HasValue)
        {
            return "/api/v1/qr/{token}";
        }

        return path.Value ?? string.Empty;
    }

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        SkipEnabledCheck = true,
        Message = "HTTP {Method} {Path} completed {StatusCode} in {ElapsedMilliseconds} ms; correlation {CorrelationId}")]
    private static partial void LogRequestCompleted(
        ILogger logger,
        string method,
        string path,
        int statusCode,
        double elapsedMilliseconds,
        string correlationId);
}
