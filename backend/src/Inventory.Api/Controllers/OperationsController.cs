using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/operations")]
[Produces("application/json")]
[Authorize(Policy = Permissions.SettingsManage)]
public sealed class OperationsController : ControllerBase
{
    private readonly OperationalStatusService _statusService;

    public OperationsController(OperationalStatusService statusService)
    {
        _statusService = statusService;
    }

    [HttpGet("status", Name = "GetOperationalStatusV1")]
    [ProducesResponseType<OperationalStatusResponse>(StatusCodes.Status200OK)]
    public ActionResult<OperationalStatusResponse> GetStatus()
    {
        var status = _statusService.GetStatus();
        var now = DateTimeOffset.UtcNow;
        return Ok(
            new OperationalStatusResponse(
                new OperationalMetricsResponse(
                    status.Metrics.StartedAtUtc,
                    Math.Max(
                        0,
                        (long)(now - status.Metrics.StartedAtUtc).TotalSeconds),
                    status.Metrics.FailedLogins,
                    status.Metrics.FailedMfaChallenges,
                    status.Metrics.ImportRejections,
                    status.Metrics.QrFailures,
                    status.Metrics.SqliteContentionEvents,
                    status.Metrics.ReadinessFailures),
                new OperationalStorageResponse(
                    status.Storage.Configured,
                    status.Storage.AvailableFreeSpaceBytes,
                    status.Storage.DatabaseConfigured,
                    status.Storage.DatabaseAvailableFreeSpaceBytes,
                    status.Storage.LatestBackupAtUtc,
                    status.Storage.LatestBackupAgeHours),
                status.Alerts
                    .Select(
                        alert => new OperationalAlertResponse(
                            alert.Severity,
                            alert.Code,
                            alert.Message))
                    .ToArray()));
    }
}
