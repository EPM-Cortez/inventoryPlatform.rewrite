using Inventory.Api.Mapping;
using Inventory.Application.Common;
using Inventory.Application.Dashboard;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Produces("application/json")]
public sealed class DashboardController : ControllerBase
{
    private readonly DashboardService _dashboardService;

    public DashboardController(DashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("summary", Name = "GetDashboardSummaryV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [ProducesResponseType<DashboardSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DashboardSummaryResponse>> Summary(
        [FromQuery] int? categoryLimit,
        CancellationToken cancellationToken)
    {
        var summary = await _dashboardService.GetSummaryAsync(
            categoryLimit,
            cancellationToken);
        return Ok(
            new DashboardSummaryResponse(
                summary.TotalAssets,
                summary.ActiveAssets,
                summary.StatusCounts
                    .Select(
                        item => new DashboardValueCountResponse<AssetStatusContract>(
                            item.Value.ToContract(),
                            item.Count))
                    .ToArray(),
                summary.ConditionCounts
                    .Select(
                        item => new DashboardValueCountResponse<AssetConditionContract>(
                            item.Value.ToContract(),
                            item.Count))
                    .ToArray(),
                summary.CategoryCounts
                    .Select(
                        item => new DashboardCategoryCountResponse(
                            item.CategoryId,
                            item.CategoryCode,
                            item.CategoryName,
                            item.Count))
                    .ToArray(),
                summary.CategoryCountsTruncated,
                summary.ExceptionCounts
                    .Select(
                        item => new DashboardExceptionCountResponse(
                            item.Code,
                            item.Label,
                            item.Count))
                    .ToArray()));
    }

    [HttpGet("inventory-by-location", Name = "GetInventoryByCategoryAndLocationV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [ProducesResponseType<InventoryDistributionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InventoryDistributionResponse>> InventoryByLocation(
        [FromQuery] Guid? categoryId,
        [FromQuery] string? query,
        [FromQuery] bool includeTerminalAssets = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _dashboardService.GetInventoryDistributionAsync(
            new InventoryDistributionSearch(
                categoryId,
                query,
                includeTerminalAssets,
                page,
                pageSize),
            cancellationToken);
        return Ok(
            new InventoryDistributionResponse(
                new PagedResponse<InventoryLocationCountResponse>(
                    result.Groups.Items
                        .Select(
                            item => new InventoryLocationCountResponse(
                                item.CategoryId,
                                item.CategoryCode,
                                item.CategoryName,
                                item.LocationId,
                                item.LocationCode,
                                item.LocationName,
                                item.Quantity,
                                item.InStockQuantity,
                                item.AssignedQuantity,
                                item.OtherQuantity))
                        .ToArray(),
                    result.Groups.Page,
                    result.Groups.PageSize,
                    result.Groups.TotalCount),
                result.MatchingAssetCount));
    }

    [HttpGet("exceptions/{exceptionCode}", Name = "GetDashboardExceptionQueueV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [ProducesResponseType<PagedResponse<DashboardExceptionAssetResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<DashboardExceptionAssetResponse>>> Exceptions(
        string exceptionCode,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        PageResult<DashboardExceptionAssetView> result =
            await _dashboardService.GetExceptionQueueAsync(
                exceptionCode,
                page,
                pageSize,
                cancellationToken);
        return Ok(
            new PagedResponse<DashboardExceptionAssetResponse>(
                result.Items
                    .Select(
                        item => new DashboardExceptionAssetResponse(
                            item.AssetId,
                            item.AssetTag,
                            item.DeviceName,
                            new ReferenceSummaryResponse(
                                item.CategoryId,
                                item.CategoryCode,
                                item.CategoryName),
                            item.Status.ToContract(),
                            item.Condition.ToContract(),
                            item.LocationId.HasValue
                                ? new LocationSummaryResponse(
                                    item.LocationId.Value,
                                    item.LocationCode ?? string.Empty,
                                    item.LocationName ?? string.Empty)
                                : null,
                            item.UpdatedAtUtc))
                    .ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }
}
