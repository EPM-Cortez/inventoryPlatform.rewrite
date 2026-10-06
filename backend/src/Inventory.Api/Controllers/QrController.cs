using Inventory.Api.Mapping;
using Inventory.Application.Qr;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/qr")]
[Produces("application/json")]
public sealed class QrController : ControllerBase
{
    private readonly QrService _qrService;

    public QrController(QrService qrService)
    {
        _qrService = qrService;
    }

    [HttpGet("{token}", Name = "ResolveAssetQrV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [EnableRateLimiting("qr-resolution")]
    [ProducesResponseType<QrResolutionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<QrResolutionResponse>> Resolve(
        string token,
        CancellationToken cancellationToken)
    {
        var asset = await _qrService.ResolveAsync(token, cancellationToken);
        return Ok(asset.ToContract());
    }
}
