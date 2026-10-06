using Inventory.Api.Mapping;
using Inventory.Application.Security;
using Inventory.Application.Warranties;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/assets/{assetId:guid}/warranties")]
[Produces("application/json")]
public sealed class AssetWarrantiesController : ControllerBase
{
    private readonly WarrantyService _warrantyService;

    public AssetWarrantiesController(WarrantyService warrantyService)
    {
        _warrantyService = warrantyService;
    }

    [HttpGet(Name = "ListAssetWarrantiesV1")]
    [Authorize(Policy = Permissions.WarrantiesRead)]
    [ProducesResponseType<IReadOnlyList<WarrantyResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<WarrantyResponse>>> List(
        Guid assetId,
        [FromQuery] bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var warranties = await _warrantyService.ListForAssetAsync(
            assetId,
            includeArchived,
            cancellationToken);
        return Ok(warranties.Select(warranty => warranty.ToContract()).ToArray());
    }

    [HttpPost(Name = "AddAssetWarrantyV1")]
    [Authorize(Policy = Permissions.WarrantiesManage)]
    [ProducesResponseType<WarrantyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WarrantyResponse>> Add(
        Guid assetId,
        [FromBody] CreateWarrantyRequest request,
        CancellationToken cancellationToken)
    {
        var warranty = await _warrantyService.AddAsync(
            assetId,
            new CreateWarrantyCommand(
                request.Provider,
                request.CoverageType,
                request.StartDate,
                request.EndDate,
                request.Reference,
                request.Notes,
                request.IsPrimary),
            cancellationToken);
        return Created(
            $"/api/v1/warranties/{warranty.Id}",
            warranty.ToContract());
    }
}
