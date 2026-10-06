using Inventory.Api.Mapping;
using Inventory.Application.Bundles;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/asset-bundles")]
[Produces("application/json")]
public sealed class AssetBundlesController : ControllerBase
{
    private readonly AssetBundleService _service;

    public AssetBundlesController(AssetBundleService service)
    {
        _service = service;
    }

    [HttpGet(Name = "ListAssetBundlesV1")]
    [Authorize(Policy = Permissions.BundlesRead)]
    [ProducesResponseType<PagedResponse<AssetBundleListItemResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<AssetBundleListItemResponse>>> List(
        [FromQuery] bool includeArchived = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.SearchAsync(
            includeArchived,
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<AssetBundleListItemResponse>(
                result.Items.Select(item => item.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpGet("{bundleId:guid}", Name = "GetAssetBundleV1")]
    [Authorize(Policy = Permissions.BundlesRead)]
    [ProducesResponseType<AssetBundleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetBundleResponse>> Get(
        Guid bundleId,
        CancellationToken cancellationToken)
    {
        var bundle = await _service.GetAsync(bundleId, cancellationToken);
        return Ok(bundle.ToContract());
    }

    [HttpPost(Name = "CreateAssetBundleV1")]
    [Authorize(Policy = Permissions.BundlesManage)]
    [ProducesResponseType<AssetBundleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetBundleResponse>> Create(
        [FromBody] CreateAssetBundleRequest request,
        CancellationToken cancellationToken)
    {
        var bundle = await _service.CreateAsync(
            new CreateAssetBundleCommand(
                request.Code,
                request.Name,
                request.Description,
                ToCommands(request.Members)),
            cancellationToken);
        return CreatedAtAction(
            nameof(Get),
            new { bundleId = bundle.Id },
            bundle.ToContract());
    }

    [HttpPut("{bundleId:guid}", Name = "UpdateAssetBundleV1")]
    [Authorize(Policy = Permissions.BundlesManage)]
    [ProducesResponseType<AssetBundleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetBundleResponse>> Update(
        Guid bundleId,
        [FromBody] UpdateAssetBundleRequest request,
        CancellationToken cancellationToken)
    {
        var bundle = await _service.UpdateAsync(
            bundleId,
            new UpdateAssetBundleCommand(
                request.Code,
                request.Name,
                request.Description,
                ToCommands(request.Members),
                request.Version),
            cancellationToken);
        return Ok(bundle.ToContract());
    }

    [HttpPost("{bundleId:guid}/archive", Name = "ArchiveAssetBundleV1")]
    [Authorize(Policy = Permissions.BundlesManage)]
    [ProducesResponseType<AssetBundleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetBundleResponse>> Archive(
        Guid bundleId,
        [FromBody] VersionRequest request,
        CancellationToken cancellationToken)
    {
        var bundle = await _service.ArchiveAsync(
            bundleId,
            request.Version,
            cancellationToken);
        return Ok(bundle.ToContract());
    }

    [HttpPost("{bundleId:guid}/restore", Name = "RestoreAssetBundleV1")]
    [Authorize(Policy = Permissions.BundlesManage)]
    [ProducesResponseType<AssetBundleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetBundleResponse>> Restore(
        Guid bundleId,
        [FromBody] VersionRequest request,
        CancellationToken cancellationToken)
    {
        var bundle = await _service.RestoreAsync(
            bundleId,
            request.Version,
            cancellationToken);
        return Ok(bundle.ToContract());
    }

    private static AssetBundleMemberCommand[] ToCommands(
        IReadOnlyCollection<AssetBundleMemberRequest> members) =>
        members
            .Select(
                member => new AssetBundleMemberCommand(
                    member.AssetId,
                    member.Role.ToDomain(),
                    member.OtherRole))
            .ToArray();
}
