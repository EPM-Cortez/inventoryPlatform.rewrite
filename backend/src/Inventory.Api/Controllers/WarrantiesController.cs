using Inventory.Api.Mapping;
using Inventory.Application.Common;
using Inventory.Application.Security;
using Inventory.Application.Warranties;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/warranties")]
[Produces("application/json")]
public sealed class WarrantiesController : ControllerBase
{
    private readonly WarrantyService _warrantyService;

    public WarrantiesController(WarrantyService warrantyService)
    {
        _warrantyService = warrantyService;
    }

    [HttpPut("{warrantyId:guid}", Name = "UpdateWarrantyV1")]
    [Authorize(Policy = Permissions.WarrantiesManage)]
    [ProducesResponseType<WarrantyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WarrantyResponse>> Update(
        Guid warrantyId,
        [FromBody] UpdateWarrantyRequest request,
        CancellationToken cancellationToken)
    {
        var warranty = await _warrantyService.UpdateAsync(
            warrantyId,
            new UpdateWarrantyCommand(
                request.Provider,
                request.CoverageType,
                request.StartDate,
                request.EndDate,
                request.Reference,
                request.Notes,
                request.IsPrimary,
                request.Version),
            cancellationToken);
        return Ok(warranty.ToContract());
    }

    [HttpPost("{warrantyId:guid}/archive", Name = "ArchiveWarrantyV1")]
    [Authorize(Policy = Permissions.WarrantiesManage)]
    [ProducesResponseType<WarrantyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WarrantyResponse>> Archive(
        Guid warrantyId,
        [FromBody] VersionRequest request,
        CancellationToken cancellationToken)
    {
        var warranty = await _warrantyService.ArchiveAsync(
            warrantyId,
            request.Version,
            cancellationToken);
        return Ok(warranty.ToContract());
    }

    [HttpGet("expiring", Name = "ListExpiringWarrantiesV1")]
    [Authorize(Policy = Permissions.WarrantiesRead)]
    [ProducesResponseType<PagedResponse<WarrantyResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<WarrantyResponse>>> Expiring(
        [FromQuery] int? withinDays,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        PageResult<WarrantyView> warranties = await _warrantyService.ListExpiringAsync(
            withinDays,
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<WarrantyResponse>(
                warranties.Items
                    .Select(warranty => warranty.ToContract())
                    .ToArray(),
                warranties.Page,
                warranties.PageSize,
                warranties.TotalCount));
    }
}
