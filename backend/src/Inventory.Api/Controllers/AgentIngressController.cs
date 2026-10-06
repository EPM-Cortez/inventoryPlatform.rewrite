using Inventory.Api.Mapping;
using Inventory.Application.Agents;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/agent")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Consumes("application/json")]
[Produces("application/json")]
public sealed class AgentIngressController : ControllerBase
{
    private readonly AgentService _service;

    public AgentIngressController(AgentService service)
    {
        _service = service;
    }

    [HttpPost("enroll", Name = "EnrollAgentV1")]
    [AllowAnonymous]
    [EnableRateLimiting("agent-enrollment")]
    [RequestSizeLimit(32_768)]
    [ProducesResponseType<AgentEnrollmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AgentEnrollmentResponse>> Enroll(
        [FromBody] AgentEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.EnrollAsync(
            new EnrollAgentCommand(
                request.EnrollmentToken,
                request.TimestampEpochSeconds,
                request.Nonce,
                request.DeviceId,
                request.PublicKeyPem,
                request.SignatureAlgorithm,
                request.KeyProtection.ToDomain(),
                request.KeyThumbprintSha256,
                request.Identity.ToApplication(),
                request.Signature,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                HttpContext.TraceIdentifier),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return StatusCode(
            StatusCodes.Status201Created,
            new AgentEnrollmentResponse(
                result.AgentId,
                result.DeviceId,
                result.ServerTimeUtc,
                result.NextSequence,
                result.CheckInIntervalSeconds));
    }

    [HttpPost("check-ins", Name = "CheckInAgentV1")]
    [AllowAnonymous]
    [EnableRateLimiting("agent-check-in")]
    [RequestSizeLimit(AgentProtocol.MaximumSnapshotBytes + 65_536)]
    [ProducesResponseType<AgentCheckInResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AgentCheckInResponse>> CheckIn(
        [FromBody] AgentCheckInRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CheckInAsync(
            new CheckInAgentCommand(
                request.AgentId,
                request.DeviceId,
                request.Sequence,
                request.TimestampEpochSeconds,
                request.Nonce,
                request.BodySha256,
                request.Signature,
                request.Snapshot.ToApplication(),
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                HttpContext.TraceIdentifier),
            cancellationToken);
        return Ok(
            new AgentCheckInResponse(
                result.Accepted,
                result.Duplicate,
                result.AgentId,
                result.SnapshotId,
                result.ReceivedAtUtc,
                result.NextSequence,
                result.LinkedAssetId,
                result.CheckInIntervalSeconds));
    }
}
