using Inventory.Api.Mapping;
using Inventory.Application.Assets;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/assets/{assetId:guid}/history")]
[Produces("application/json")]
public sealed class AssetHistoryController : ControllerBase
{
    private readonly AssetService _assetService;

    public AssetHistoryController(AssetService assetService)
    {
        _assetService = assetService;
    }

    [HttpGet(Name = "GetAssetHistoryV1")]
    [Authorize(Policy = Permissions.HistoryRead)]
    [ProducesResponseType<PagedResponse<AssetEventResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<AssetEventResponse>>> List(
        Guid assetId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _assetService.GetHistoryAsync(
            assetId,
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<AssetEventResponse>(
                result.Items.Select(item => item.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpPost("notes", Name = "AddAssetHistoryNoteV1")]
    [Authorize(Policy = Permissions.HistoryNote)]
    [ProducesResponseType<AssetEventResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetEventResponse>> AddNote(
        Guid assetId,
        [FromBody] AddAssetNoteRequest request,
        CancellationToken cancellationToken)
    {
        var assetEvent = await _assetService.AddNoteAsync(
            assetId,
            request.Note,
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, assetEvent.ToContract());
    }
}
