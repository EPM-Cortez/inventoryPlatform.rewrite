using Inventory.Api.Mapping;
using Inventory.Application.Agents;
using Inventory.Application.Assets;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/agents")]
[Produces("application/json")]
[ResponseCache(
    Duration = 0,
    Location = ResponseCacheLocation.None,
    NoStore = true)]
public sealed class AgentsController : ControllerBase
{
    private readonly AgentService _service;

    public AgentsController(AgentService service)
    {
        _service = service;
    }

    [HttpGet(Name = "ListManagedAgentsV1")]
    [Authorize(Policy = Permissions.AgentsRead)]
    [ProducesResponseType<PagedResponse<ManagedAgentListItemResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    public async Task<
        ActionResult<PagedResponse<ManagedAgentListItemResponse>>> List(
        [FromQuery] string? q = null,
        [FromQuery] bool includeRevoked = false,
        [FromQuery] ManagedAgentStatusContract? trustStatus = null,
        [FromQuery] ManagedAgentMatchStateContract? matchState = null,
        [FromQuery] ManagedAgentConnectivityStatusContract? connectivity = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.SearchAsync(
            q,
            includeRevoked,
            trustStatus switch
            {
                ManagedAgentStatusContract.Active => false,
                ManagedAgentStatusContract.Revoked => true,
                null => null,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(trustStatus),
                    trustStatus,
                    null),
            },
            matchState?.ToDomain(),
            connectivity?.ToDomain(),
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<ManagedAgentListItemResponse>(
                result.Items.Select(item => item.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpGet("summary", Name = "GetManagedAgentFleetSummaryV1")]
    [Authorize(Policy = Permissions.AgentsRead)]
    [ProducesResponseType<ManagedAgentFleetSummaryResponse>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<ManagedAgentFleetSummaryResponse>> Summary(
        [FromQuery] bool includeRevoked = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.GetFleetSummaryAsync(
            includeRevoked,
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpGet("{agentId:guid}", Name = "GetManagedAgentV1")]
    [Authorize(Policy = Permissions.AgentsRead)]
    [ProducesResponseType<ManagedAgentDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedAgentDetailResponse>> Get(
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(agentId, cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpGet("/api/v1/assets/{assetId:guid}/device-inventory",
        Name = "GetAssetDeviceInventoryV1")]
    [Authorize(Policy = Permissions.AgentsRead)]
    [ProducesResponseType<ManagedAgentDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedAgentDetailResponse>>
        GetForAsset(
            Guid assetId,
            CancellationToken cancellationToken)
    {
        var result = await _service.GetByAssetIdAsync(
            assetId,
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpGet("enrollment-profiles",
        Name = "ListAgentEnrollmentProfilesV1")]
    [Authorize(Policy = Permissions.AgentsRead)]
    [ProducesResponseType<PagedResponse<AgentEnrollmentProfileResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    public async Task<
        ActionResult<PagedResponse<AgentEnrollmentProfileResponse>>>
        ListEnrollmentProfiles(
            [FromQuery] bool includeRevoked = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
    {
        var result = await _service.SearchEnrollmentProfilesAsync(
            includeRevoked,
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<AgentEnrollmentProfileResponse>(
                result.Items.Select(item => item.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpPost("enrollment-profiles",
        Name = "CreateAgentEnrollmentProfileV1")]
    [Authorize(Policy = Permissions.AgentsManage)]
    [ProducesResponseType<CreatedAgentEnrollmentProfileResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedAgentEnrollmentProfileResponse>>
        CreateEnrollmentProfile(
            [FromBody] CreateAgentEnrollmentProfileRequest request,
            CancellationToken cancellationToken)
    {
        var result = await _service.CreateEnrollmentProfileAsync(
            new CreateAgentEnrollmentProfileCommand(
                request.Name,
                request.ExpiresAtUtc,
                request.MaxEnrollments,
                request.RequireTpm,
                request.AllowedHostnameSuffix),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return StatusCode(
            StatusCodes.Status201Created,
            new CreatedAgentEnrollmentProfileResponse(
                result.Profile.ToContract(),
                result.EnrollmentToken));
    }

    [HttpPost("enrollment-profiles/{profileId:guid}/revoke",
        Name = "RevokeAgentEnrollmentProfileV1")]
    [Authorize(Policy = Permissions.AgentsManage)]
    [ProducesResponseType<AgentEnrollmentProfileResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AgentEnrollmentProfileResponse>>
        RevokeEnrollmentProfile(
            Guid profileId,
            [FromBody] RevokeAgentEnrollmentProfileRequest request,
            CancellationToken cancellationToken)
    {
        var result = await _service.RevokeEnrollmentProfileAsync(
            profileId,
            request.Version,
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost("{agentId:guid}/link", Name = "LinkManagedAgentV1")]
    [Authorize(Policy = Permissions.AgentsManage)]
    [ProducesResponseType<ManagedAgentDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManagedAgentDetailResponse>> Link(
        Guid agentId,
        [FromBody] LinkManagedAgentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.LinkAsync(
            new LinkManagedAgentCommand(
                agentId,
                request.AssetId,
                request.Reason,
                request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost("{agentId:guid}/unlink", Name = "UnlinkManagedAgentV1")]
    [Authorize(Policy = Permissions.AgentsManage)]
    [ProducesResponseType<ManagedAgentDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManagedAgentDetailResponse>> Unlink(
        Guid agentId,
        [FromBody] UnlinkManagedAgentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UnlinkAsync(
            new UnlinkManagedAgentCommand(
                agentId,
                request.Reason,
                request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost("{agentId:guid}/dismiss", Name = "DismissManagedAgentV1")]
    [Authorize(Policy = Permissions.AgentsManage)]
    [ProducesResponseType<ManagedAgentDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManagedAgentDetailResponse>> Dismiss(
        Guid agentId,
        [FromBody] DismissManagedAgentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.DismissAsync(
            new DismissManagedAgentCommand(
                agentId,
                request.Reason,
                request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost(
        "{agentId:guid}/restore-review",
        Name = "RestoreManagedAgentReviewV1")]
    [Authorize(Policy = Permissions.AgentsManage)]
    [ProducesResponseType<ManagedAgentDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManagedAgentDetailResponse>> RestoreReview(
        Guid agentId,
        [FromBody] RestoreManagedAgentReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RestoreReviewAsync(
            new RestoreManagedAgentReviewCommand(
                agentId,
                request.Reason,
                request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost(
        "{agentId:guid}/create-and-link-asset",
        Name = "CreateAndLinkManagedAgentAssetV1")]
    [Authorize(Policy = Permissions.AgentsManage)]
    [Authorize(Policy = Permissions.AssetsCreate)]
    [ProducesResponseType<CreateAndLinkManagedAgentAssetResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateAndLinkManagedAgentAssetResponse>>
        CreateAndLinkAsset(
            Guid agentId,
            [FromBody] CreateAndLinkManagedAgentAssetRequest request,
            CancellationToken cancellationToken)
    {
        var result = await _service.CreateAndLinkAssetAsync(
            new CreateAndLinkManagedAgentAssetCommand(
                agentId,
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
                request.Reason,
                request.Version),
            cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            new CreateAndLinkManagedAgentAssetResponse(
                result.Asset.ToContract(),
                result.Agent.ToContract()));
    }

    [HttpPost("{agentId:guid}/revoke", Name = "RevokeManagedAgentV1")]
    [Authorize(Policy = Permissions.AgentsManage)]
    [ProducesResponseType<ManagedAgentDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ManagedAgentDetailResponse>> Revoke(
        Guid agentId,
        [FromBody] RevokeManagedAgentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RevokeAsync(
            new RevokeManagedAgentCommand(
                agentId,
                request.Reason,
                request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost(
        "{agentId:guid}/key-recovery",
        Name = "CreateAgentKeyRecoveryV1")]
    [Authorize(Policy = Permissions.AgentsRecover)]
    [ProducesResponseType<CreatedAgentKeyRecoveryResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedAgentKeyRecoveryResponse>>
        CreateKeyRecovery(
            Guid agentId,
            [FromBody] CreateAgentKeyRecoveryRequest request,
            CancellationToken cancellationToken)
    {
        var result = await _service.CreateKeyRecoveryAsync(
            new CreateAgentKeyRecoveryCommand(
                agentId,
                request.Reason,
                request.ExpiresAtUtc,
                request.RequireTpm,
                request.Version),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return StatusCode(
            StatusCodes.Status201Created,
            new CreatedAgentKeyRecoveryResponse(
                result.AuthorizationId,
                result.AgentId,
                result.ExpiresAtUtc,
                result.RequireTpm,
                result.RecoveryToken,
                result.Version));
    }

    [HttpGet(
        "{agentId:guid}/key-recovery",
        Name = "ListAgentKeyRecoveryAuthorizationsV1")]
    [Authorize(Policy = Permissions.AgentsRecover)]
    [ProducesResponseType<
        PagedResponse<AgentKeyRecoveryAuthorizationResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        PagedResponse<AgentKeyRecoveryAuthorizationResponse>>>
        ListKeyRecoveryAuthorizations(
            Guid agentId,
            [FromQuery] bool includeClosed = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
    {
        var result =
            await _service.SearchKeyRecoveryAuthorizationsAsync(
                agentId,
                includeClosed,
                page,
                pageSize,
                cancellationToken);
        return Ok(
            new PagedResponse<AgentKeyRecoveryAuthorizationResponse>(
                result.Items.Select(ToContract).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpPost(
        "{agentId:guid}/key-recovery/{authorizationId:guid}/revoke",
        Name = "RevokeAgentKeyRecoveryV1")]
    [Authorize(Policy = Permissions.AgentsRecover)]
    [ProducesResponseType<AgentKeyRecoveryAuthorizationResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AgentKeyRecoveryAuthorizationResponse>>
        RevokeKeyRecovery(
            Guid agentId,
            Guid authorizationId,
            [FromBody] RevokeAgentKeyRecoveryRequest request,
            CancellationToken cancellationToken)
    {
        var result = await _service.RevokeKeyRecoveryAsync(
            new RevokeAgentKeyRecoveryCommand(
                agentId,
                authorizationId,
                request.Reason,
                request.Version),
            cancellationToken);
        return Ok(
            ToContract(result));
    }

    private static AgentKeyRecoveryAuthorizationResponse ToContract(
        AgentKeyRecoveryAuthorizationView result) =>
        new(
            result.AuthorizationId,
            result.AgentId,
            (AgentKeyRecoveryAuthorizationStatusContract)result.Status,
            result.Reason,
            result.CreatedAtUtc,
            result.CreatedByUserId,
            result.CreatedByDisplayName,
            result.ExpiresAtUtc,
            result.RequireTpm,
            result.ConsumedAtUtc,
            result.RevokedAtUtc,
            result.RevokedByUserId,
            result.RevokedReason,
            result.Version);
}
