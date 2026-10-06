using Inventory.Application.Common;
using Inventory.Domain.Common;
using Inventory.Infrastructure.Observability;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Npgsql;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Errors;

public sealed partial class InventoryExceptionHandler : IExceptionHandler
{
    private readonly ILogger<InventoryExceptionHandler> _logger;
    private readonly IOperationalMetrics _metrics;

    public InventoryExceptionHandler(
        ILogger<InventoryExceptionHandler> logger,
        IOperationalMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, type, title, detail, errors) = exception switch
        {
            ResourceNotFoundException notFound => (
                StatusCodes.Status404NotFound,
                "https://inventory.local/problems/not-found",
                "The requested resource was not found.",
                notFound.Message,
                (IReadOnlyDictionary<string, string[]>?)null),
            PermissionDeniedException denied => (
                StatusCodes.Status403Forbidden,
                "https://inventory.local/problems/permission-denied",
                "Permission denied.",
                denied.Message,
                (IReadOnlyDictionary<string, string[]>?)null),
            RequestAuthenticationException authentication => (
                StatusCodes.Status401Unauthorized,
                $"https://inventory.local/problems/{authentication.Code.Replace('_', '-')}",
                "The endpoint agent could not be authenticated.",
                authentication.Message,
                (IReadOnlyDictionary<string, string[]>?)null),
            RequestRateLimitException rateLimit => (
                StatusCodes.Status429TooManyRequests,
                $"https://inventory.local/problems/{rateLimit.Code.Replace('_', '-')}",
                "The endpoint agent check-in rate is too high.",
                rateLimit.Message,
                (IReadOnlyDictionary<string, string[]>?)null),
            ResourceConflictException conflict => (
                StatusCodes.Status409Conflict,
                $"https://inventory.local/problems/{conflict.Code.Replace('_', '-')}",
                conflict.Code == "concurrency_conflict"
                    ? "The record was changed by another user."
                    : "The requested change conflicts with existing data.",
                conflict.Message,
                (IReadOnlyDictionary<string, string[]>?)null),
            RequestValidationException validation => (
                StatusCodes.Status400BadRequest,
                $"https://inventory.local/problems/{validation.Code.Replace('_', '-')}",
                "The request could not be completed.",
                validation.Message,
                validation.Errors),
            AntiforgeryValidationException => (
                StatusCodes.Status400BadRequest,
                "https://inventory.local/problems/antiforgery-validation-failed",
                "The antiforgery token was not accepted.",
                "Obtain a fresh antiforgery token and retry the request.",
                (IReadOnlyDictionary<string, string[]>?)null),
            DomainRuleException domain => (
                StatusCodes.Status400BadRequest,
                $"https://inventory.local/problems/{domain.Code.Replace('_', '-')}",
                "The request violates an inventory rule.",
                domain.Message,
                (IReadOnlyDictionary<string, string[]>?)null),
            _ => (
                StatusCodes.Status500InternalServerError,
                "https://inventory.local/problems/internal-error",
                "An unexpected error occurred.",
                "The server could not complete the request.",
                (IReadOnlyDictionary<string, string[]>?)null),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            if (IsDatabaseContention(exception.GetBaseException()))
            {
                _metrics.RecordSqliteContention();
            }

            LogUnhandledRequest(exception, httpContext.TraceIdentifier);
        }
        else
        {
            LogRejectedRequest(exception, status, type, exception.Message);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Type = type,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path.StartsWithSegments(
                "/api/v1/qr",
                StringComparison.OrdinalIgnoreCase)
                ? "/api/v1/qr/{token}"
                : httpContext.Request.Path,
        };
        problem.Extensions["code"] = exception switch
        {
            RequestAuthenticationException authentication =>
                authentication.Code,
            RequestRateLimitException rateLimit => rateLimit.Code,
            ResourceConflictException conflict => conflict.Code,
            RequestValidationException validation => validation.Code,
            DomainRuleException domain => domain.Code,
            ResourceNotFoundException => "not_found",
            PermissionDeniedException => "permission_denied",
            AntiforgeryValidationException =>
                "antiforgery_validation_failed",
            _ => "internal_error",
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors;
        }

        if (exception is RequestRateLimitException rateLimitException)
        {
            httpContext.Response.Headers.RetryAfter =
                rateLimitException.RetryAfterSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: (JsonSerializerOptions?)null,
            contentType: "application/problem+json",
            cancellationToken);
        return true;
    }

    private static bool IsDatabaseContention(Exception exception) =>
        exception is SqliteException
        {
            SqliteErrorCode: 5 or 6,
        } or PostgresException
        {
            SqlState: "40001" or "40P01" or "55P03",
        };

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Error,
        Message = "Unhandled request failure for {TraceIdentifier}")]
    private partial void LogUnhandledRequest(Exception exception, string traceIdentifier);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Request rejected with {StatusCode} and problem type {ProblemType}: {Message}")]
    private partial void LogRejectedRequest(
        Exception exception,
        int statusCode,
        string problemType,
        string message);
}
