using Inventory.Api.Mapping;
using Inventory.Application.References;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/locations")]
[Produces("application/json")]
public sealed class LocationsController : ControllerBase
{
    private readonly ReferenceDataService _referenceDataService;

    public LocationsController(ReferenceDataService referenceDataService)
    {
        _referenceDataService = referenceDataService;
    }

    [HttpGet(Name = "ListLocationsV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [ProducesResponseType<PagedResponse<LocationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<LocationResponse>>> List(
        [FromQuery] string? q,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _referenceDataService.ListLocationsAsync(
            q,
            includeInactive,
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<LocationResponse>(
                result.Items.Select(location => location.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpGet("{locationId:guid}", Name = "GetLocationV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [ProducesResponseType<LocationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LocationResponse>> Get(
        Guid locationId,
        CancellationToken cancellationToken)
    {
        var location = await _referenceDataService.GetLocationAsync(
            locationId,
            cancellationToken);
        return Ok(location.ToContract());
    }

    [HttpPost(Name = "CreateLocationV1")]
    [Authorize(Policy = Permissions.ReferenceManage)]
    [ProducesResponseType<LocationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LocationResponse>> Create(
        [FromBody] CreateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var location = await _referenceDataService.CreateLocationAsync(
            new CreateLocationCommand(
                request.Code,
                request.Name,
                request.Type,
                request.ParentLocationId),
            cancellationToken);
        return CreatedAtAction(
            nameof(Get),
            new { locationId = location.Id },
            location.ToContract());
    }

    [HttpPut("{locationId:guid}", Name = "UpdateLocationV1")]
    [Authorize(Policy = Permissions.ReferenceManage)]
    [ProducesResponseType<LocationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LocationResponse>> Update(
        Guid locationId,
        [FromBody] UpdateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var location = await _referenceDataService.UpdateLocationAsync(
            locationId,
            new UpdateLocationCommand(
                request.Code,
                request.Name,
                request.Type,
                request.ParentLocationId,
                request.IsActive,
                request.Version),
            cancellationToken);
        return Ok(location.ToContract());
    }
}
