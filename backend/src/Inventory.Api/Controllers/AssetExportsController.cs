using Inventory.Api.Mapping;
using Inventory.Application.DataExchange;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/exports")]
public sealed class AssetExportsController : ControllerBase
{
    private readonly ImportExportService _service;

    public AssetExportsController(ImportExportService service)
    {
        _service = service;
    }

    [HttpGet("assets.csv", Name = "ExportAssetsCsvV1")]
    [Authorize(Policy = Permissions.ExportsAssets)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportAssets(
        [FromQuery] string? q,
        [FromQuery] string? assetTag,
        [FromQuery] string? serialNumber,
        [FromQuery] Guid? categoryId,
        [FromQuery] AssetStatusContract? status,
        [FromQuery] AssetConditionContract? condition,
        [FromQuery] Guid? locationId,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? assignedPersonId,
        [FromQuery] bool? assigned,
        [FromQuery] WarrantyStateContract? warrantyState,
        [FromQuery] DateOnly? warrantyEndsBefore,
        [FromQuery] DateTimeOffset? createdFrom,
        [FromQuery] DateTimeOffset? createdTo,
        [FromQuery] string? legacyId,
        [FromQuery] string sort = "assetTag",
        CancellationToken cancellationToken = default)
    {
        var file = await _service.ExportAssetsAsync(
            new AssetExportFilter(
                q,
                assetTag,
                serialNumber,
                categoryId,
                status?.ToDomain(),
                condition?.ToDomain(),
                locationId,
                departmentId,
                assignedPersonId,
                assigned,
                warrantyState?.ToDomain(),
                warrantyEndsBefore,
                createdFrom,
                createdTo,
                legacyId,
                sort),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Append(
            "X-Inventory-Row-Count",
            file.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return File(file.Content, "text/csv; charset=utf-8", file.FileName);
    }
}
