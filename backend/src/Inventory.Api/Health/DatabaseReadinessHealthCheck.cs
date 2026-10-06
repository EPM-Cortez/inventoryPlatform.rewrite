using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Inventory.Infrastructure.Observability;

namespace Inventory.Api.Health;

public sealed class DatabaseReadinessHealthCheck : IHealthCheck
{
    private readonly InventoryDbContext _dbContext;
    private readonly IOperationalMetrics _metrics;

    public DatabaseReadinessHealthCheck(
        InventoryDbContext dbContext,
        IOperationalMetrics metrics)
    {
        _dbContext = dbContext;
        _metrics = metrics;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _dbContext.Database.CanConnectAsync(cancellationToken))
            {
                _metrics.RecordReadinessFailure();
                return HealthCheckResult.Unhealthy(
                    "The inventory database is unavailable.");
            }

            var pendingMigrations = await _dbContext.Database
                .GetPendingMigrationsAsync(cancellationToken);
            if (pendingMigrations.Any())
            {
                _metrics.RecordReadinessFailure();
                return HealthCheckResult.Unhealthy(
                    "The inventory database has pending migrations.",
                    data: new Dictionary<string, object>
                    {
                        ["pendingMigrationCount"] = pendingMigrations.Count(),
                    });
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            _metrics.RecordReadinessFailure();
            return HealthCheckResult.Unhealthy(
                "The inventory database readiness check failed.",
                exception);
        }
    }
}
