using Inventory.Infrastructure.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inventory.Api.Health;

public sealed class StorageReadinessHealthCheck : IHealthCheck
{
    private readonly OperationalStatusService _statusService;
    private readonly IOperationalMetrics _metrics;

    public StorageReadinessHealthCheck(
        OperationalStatusService statusService,
        IOperationalMetrics metrics)
    {
        _statusService = statusService;
        _metrics = metrics;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = _statusService.GetStatus();
        if (!status.Storage.Configured &&
            !status.Storage.DatabaseConfigured)
        {
            return Task.FromResult(
                HealthCheckResult.Healthy(
                    "Persistent backup and database storage are not configured in this environment."));
        }

        var critical = status.Alerts.Where(alert => alert.Severity == "critical").ToArray();
        if (critical.Length > 0)
        {
            _metrics.RecordReadinessFailure();
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    string.Join(" ", critical.Select(alert => alert.Message))));
        }

        var warnings = status.Alerts
            .Where(alert => alert.Code is "backup_missing" or "backup_stale")
            .ToArray();
        return warnings.Length > 0
            ? Task.FromResult(
                HealthCheckResult.Degraded(
                    string.Join(" ", warnings.Select(alert => alert.Message))))
            : Task.FromResult(HealthCheckResult.Healthy());
    }
}
