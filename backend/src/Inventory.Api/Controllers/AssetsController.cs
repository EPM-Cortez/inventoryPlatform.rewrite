using Inventory.Api.Mapping;
using Inventory.Application.Assets;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/assets")]
[Produces("application/json")]
public sealed class AssetsController : ControllerBase
{
    private readonly AssetService _assetService;

    public AssetsController(AssetService assetService)
    {
        _assetService = assetService;
    }

    [HttpGet(Name = "ListAssetsV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [ProducesResponseType<PagedResponse<AssetListItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<AssetListItemResponse>>> List(
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
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string sort = "assetTag",
        CancellationToken cancellationToken = default)
    {
        var result = await _assetService.SearchAsync(
            new AssetSearch(
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
                page,
                pageSize,
                sort),
            cancellationToken);

        return Ok(
            new PagedResponse<AssetListItemResponse>(
                result.Items.Select(item => item.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpGet("{assetId:guid}", Name = "GetAssetByIdV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetResponse>> GetById(
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var asset = await _assetService.GetAsync(assetId, cancellationToken);
        return Ok(asset.ToContract());
    }

    [HttpGet("by-tag/{assetTag}", Name = "GetAssetByTagV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetResponse>> GetByTag(
        string assetTag,
        CancellationToken cancellationToken)
    {
        var asset = await _assetService.GetByTagAsync(assetTag, cancellationToken);
        return Ok(asset.ToContract());
    }

    [HttpPost(Name = "CreateAssetV1")]
    [Authorize(Policy = Permissions.AssetsCreate)]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetResponse>> Create(
        [FromBody] CreateAssetRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await _assetService.CreateAsync(
            new CreateAssetCommand(
                request.AssetTag,
                request.DeviceName,
                request.CategoryId,
                request.Description,
                request.Manufacturer,
                request.Model,
                request.SerialNumber,
                request.LanMacAddress,
                request.BiosGuid,
                request.Ownership,
                request.Status.ToDomain(),
                request.Condition.ToDomain(),
                request.LocationId,
                request.PurchaseDate,
                request.PurchaseCost,
                request.Currency,
                request.Supplier,
                request.OperatingSystem,
                request.OperatingSystemVersion,
                request.Windows11Compliant,
                request.Notes,
                request.LegacyId,
                request.GenerateAssetTag),
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { assetId = asset.Id },
            asset.ToContract());
    }

    [HttpPut("{assetId:guid}", Name = "UpdateAssetV1")]
    [Authorize(Policy = Permissions.AssetsUpdate)]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetResponse>> Update(
        Guid assetId,
        [FromBody] UpdateAssetRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await _assetService.UpdateAsync(
            assetId,
            new UpdateAssetCommand(
                request.AssetTag,
                request.DeviceName,
                request.CategoryId,
                request.Description,
                request.Manufacturer,
                request.Model,
                request.SerialNumber,
                request.LanMacAddress,
                request.BiosGuid,
                request.Ownership,
                request.Status.ToDomain(),
                request.Condition.ToDomain(),
                request.LocationId,
                request.PurchaseDate,
                request.PurchaseCost,
                request.Currency,
                request.Supplier,
                request.OperatingSystem,
                request.OperatingSystemVersion,
                request.Windows11Compliant,
                request.Notes,
                request.LegacyId,
                request.Version),
            cancellationToken);

        return Ok(asset.ToContract());
    }

    [HttpPost("{assetId:guid}/retire", Name = "RetireAssetV1")]
    [Authorize(Policy = Permissions.AssetsRetire)]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetResponse>> Retire(
        Guid assetId,
        [FromBody] RetireAssetRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await _assetService.RetireAsync(
            assetId,
            request.Version,
            request.Reason,
            cancellationToken);
        return Ok(asset.ToContract());
    }

    [HttpPost("{assetId:guid}/dispose", Name = "DisposeAssetV1")]
    [Authorize(Policy = Permissions.AssetsRetire)]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetResponse>> Dispose(
        Guid assetId,
        [FromBody] DisposeAssetRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await _assetService.DisposeAsync(
            assetId,
            request.Version,
            request.Reason,
            cancellationToken);
        return Ok(asset.ToContract());
    }

    [HttpPost("{assetId:guid}/restore", Name = "RestoreAssetV1")]
    [Authorize(Policy = Permissions.AssetsRestore)]
    [ProducesResponseType<AssetResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetResponse>> Restore(
        Guid assetId,
        [FromBody] VersionRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await _assetService.RestoreAsync(
            assetId,
            request.Version,
            cancellationToken);
        return Ok(asset.ToContract());
    }
}
