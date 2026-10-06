using Inventory.Api.Mapping;
using Inventory.Application.Abstractions;
using Inventory.Application.Qr;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/assets/{assetId:guid}/qr")]
public sealed class AssetQrController : ControllerBase
{
    private readonly QrService _qrService;

    public AssetQrController(QrService qrService)
    {
        _qrService = qrService;
    }

    [HttpPost(Name = "IssueAssetQrV1")]
    [Authorize(Policy = Permissions.QrGenerate)]
    [ProducesResponseType<QrIdentifierResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QrIdentifierResponse>> Issue(
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var identifier = await _qrService.IssueAsync(assetId, cancellationToken);
        return CreatedAtAction(
            nameof(Render),
            new { assetId },
            identifier.ToContract());
    }

    [HttpGet(Name = "GetAssetQrV1")]
    [Authorize(Policy = Permissions.QrGenerate)]
    [Produces("image/svg+xml", "image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Render(
        Guid assetId,
        [FromQuery] QrImageFormatContract format = QrImageFormatContract.Svg,
        CancellationToken cancellationToken = default)
    {
        var image = await _qrService.RenderAsync(
            assetId,
            format == QrImageFormatContract.Png
                ? QrImageFormat.Png
                : QrImageFormat.Svg,
            cancellationToken);
        Response.Headers.CacheControl = "no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        return File(image.Content, image.ContentType, image.FileName);
    }

    [HttpPost("rotate", Name = "RotateAssetQrV1")]
    [Authorize(Policy = Permissions.QrRotate)]
    [ProducesResponseType<QrIdentifierResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<QrIdentifierResponse>> Rotate(
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var identifier = await _qrService.RotateAsync(assetId, cancellationToken);
        return Ok(identifier.ToContract());
    }
}
