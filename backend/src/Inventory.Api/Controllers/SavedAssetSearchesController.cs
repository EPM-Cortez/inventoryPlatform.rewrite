using Inventory.Api.Mapping;
using Inventory.Application.SavedSearches;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/me/saved-asset-searches")]
[Produces("application/json")]
[Authorize(Policy = Permissions.AssetsRead)]
public sealed class SavedAssetSearchesController : ControllerBase
{
    private readonly SavedSearchService _service;

    public SavedAssetSearchesController(SavedSearchService service)
    {
        _service = service;
    }

    [HttpGet(Name = "ListSavedAssetSearchesV1")]
    [ProducesResponseType<PagedResponse<SavedAssetSearchResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<SavedAssetSearchResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ListAsync(page, pageSize, cancellationToken);
        return Ok(
            new PagedResponse<SavedAssetSearchResponse>(
                result.Items.Select(item => item.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpGet("default", Name = "GetDefaultSavedAssetSearchV1")]
    [ProducesResponseType<SavedAssetSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<SavedAssetSearchResponse>> GetDefault(
        CancellationToken cancellationToken)
    {
        var result = await _service.GetDefaultAsync(cancellationToken);
        return result is null ? NoContent() : Ok(result.ToContract());
    }

    [HttpGet("{savedSearchId:guid}", Name = "GetSavedAssetSearchV1")]
    [ProducesResponseType<SavedAssetSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SavedAssetSearchResponse>> Get(
        Guid savedSearchId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(savedSearchId, cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost(Name = "CreateSavedAssetSearchV1")]
    [ProducesResponseType<SavedAssetSearchResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SavedAssetSearchResponse>> Create(
        [FromBody] CreateSavedAssetSearchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(
            new CreateSavedAssetSearchCommand(
                request.Name,
                request.SchemaVersion,
                request.Filter.ToApplication()),
            cancellationToken);
        return CreatedAtAction(
            nameof(Get),
            new { savedSearchId = result.Id },
            result.ToContract());
    }

    [HttpPut("{savedSearchId:guid}", Name = "UpdateSavedAssetSearchV1")]
    [ProducesResponseType<SavedAssetSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SavedAssetSearchResponse>> Update(
        Guid savedSearchId,
        [FromBody] UpdateSavedAssetSearchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(
            savedSearchId,
            new UpdateSavedAssetSearchCommand(
                request.Name,
                request.SchemaVersion,
                request.Filter.ToApplication(),
                request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost("{savedSearchId:guid}/default", Name = "SetDefaultSavedAssetSearchV1")]
    [ProducesResponseType<SavedAssetSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SavedAssetSearchResponse>> SetDefault(
        Guid savedSearchId,
        [FromBody] ChangeSavedAssetSearchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.SetDefaultAsync(
            savedSearchId,
            new ChangeSavedAssetSearchCommand(request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost("{savedSearchId:guid}/archive", Name = "ArchiveSavedAssetSearchV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Archive(
        Guid savedSearchId,
        [FromBody] ChangeSavedAssetSearchRequest request,
        CancellationToken cancellationToken)
    {
        await _service.ArchiveAsync(
            savedSearchId,
            new ChangeSavedAssetSearchCommand(request.Version),
            cancellationToken);
        return NoContent();
    }
}
