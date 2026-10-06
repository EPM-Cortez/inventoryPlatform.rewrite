using Inventory.Application.AssetTags;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/settings/asset-tags")]
[Produces("application/json")]
public sealed class AssetTagPolicyController : ControllerBase
{
    private readonly AssetTagPolicyService _service;

    public AssetTagPolicyController(AssetTagPolicyService service)
    {
        _service = service;
    }

    [HttpGet(Name = "GetAssetTagPolicyV1")]
    [Authorize(Policy = Permissions.AssetTagsManage)]
    [ProducesResponseType<AssetTagPolicyResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AssetTagPolicyResponse>> Get(
        CancellationToken cancellationToken)
    {
        var policy = await _service.GetAsync(cancellationToken);
        return Ok(ToContract(policy));
    }

    [HttpPut(Name = "UpdateAssetTagPolicyV1")]
    [Authorize(Policy = Permissions.AssetTagsManage)]
    [ProducesResponseType<AssetTagPolicyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetTagPolicyResponse>> Update(
        [FromBody] UpdateAssetTagPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await _service.UpdateAsync(
            new UpdateAssetTagPolicyCommand(
                request.Prefix,
                request.NumericWidth,
                request.NextNumber,
                request.Version),
            cancellationToken);
        return Ok(ToContract(policy));
    }

    private static AssetTagPolicyResponse ToContract(
        AssetTagPolicyView policy) =>
        new(
            policy.Prefix,
            policy.NumericWidth,
            policy.NextNumber,
            policy.NextAssetTag,
            policy.Version);
}
