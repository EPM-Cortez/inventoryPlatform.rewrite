using Inventory.Api.Mapping;
using Inventory.Application.BulkChanges;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/assets/bulk-changes")]
[Produces("application/json")]
public sealed class AssetBulkChangesController : ControllerBase
{
    private readonly AssetBulkChangeService _service;

    public AssetBulkChangesController(AssetBulkChangeService service)
    {
        _service = service;
    }

    [HttpPost("preview", Name = "PreviewAssetBulkChangeV1")]
    [Authorize(Policy = Permissions.AssetsBulk)]
    [EnableRateLimiting("bulk-preview")]
    [ProducesResponseType<AssetBulkChangePreviewResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetBulkChangePreviewResponse>> Preview(
        [FromBody] PreviewAssetBulkChangeRequest request,
        CancellationToken cancellationToken)
    {
        var preview = await _service.PreviewAsync(
            new PreviewAssetBulkChangeCommand(
                request.Assets
                    .Select(
                        asset => new AssetBulkChangeSelection(
                            asset.AssetId,
                            asset.Version))
                    .ToArray(),
                new AssetBulkChangeOperation(
                    request.Operation.Kind.ToDomain(),
                    request.Operation.LocationId,
                    request.Operation.ClearLocation,
                    request.Operation.CategoryId,
                    request.Operation.Condition?.ToDomain(),
                    request.Operation.Status?.ToDomain(),
                    request.Operation.Note),
                request.Reason),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, preview.ToContract());
    }

    [HttpPost("{jobId:guid}/commit", Name = "CommitAssetBulkChangeV1")]
    [Authorize(Policy = Permissions.AssetsBulk)]
    [ProducesResponseType<AssetBulkChangeCommitResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetBulkChangeCommitResponse>> Commit(
        Guid jobId,
        [FromBody] CommitAssetBulkChangeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CommitAsync(
            new CommitAssetBulkChangeCommand(
                jobId,
                request.Fingerprint,
                request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }
}
