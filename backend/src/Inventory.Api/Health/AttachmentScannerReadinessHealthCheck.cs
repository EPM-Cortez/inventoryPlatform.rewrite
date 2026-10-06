using Inventory.Application.Attachments;
using Inventory.Infrastructure.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inventory.Api.Health;

public sealed class AttachmentScannerReadinessHealthCheck : IHealthCheck
{
    private readonly IAttachmentMalwareScanner _scanner;
    private readonly IOperationalMetrics _metrics;

    public AttachmentScannerReadinessHealthCheck(
        IAttachmentMalwareScanner scanner,
        IOperationalMetrics metrics)
    {
        _scanner = scanner;
        _metrics = metrics;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var readiness = await _scanner.CheckReadinessAsync(cancellationToken);
            if (readiness.IsReady)
            {
                return HealthCheckResult.Healthy(
                    "Attachment malware scanning is ready.",
                    new Dictionary<string, object>
                    {
                        ["engine"] = readiness.Engine,
                    });
            }

            _metrics.RecordReadinessFailure();
            return HealthCheckResult.Unhealthy(
                "Attachment malware scanning is unavailable.",
                data: new Dictionary<string, object>
                {
                    ["engine"] = readiness.Engine,
                    ["detailCode"] = readiness.DetailCode,
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _metrics.RecordReadinessFailure();
            return HealthCheckResult.Unhealthy(
                "The attachment malware-scanner readiness check failed.",
                exception);
        }
    }
}
