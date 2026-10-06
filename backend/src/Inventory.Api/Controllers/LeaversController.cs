using Inventory.Api.Mapping;
using Inventory.Application.Leavers;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/leavers")]
[Produces("application/json")]
public sealed class LeaversController : ControllerBase
{
    private readonly LeaverService _service;

    public LeaversController(LeaverService service)
    {
        _service = service;
    }

    [HttpGet(Name = "ListLeaverCasesV1")]
    [Authorize(Policy = Permissions.LeaversRead)]
    [ProducesResponseType<PagedResponse<LeaverCaseSummaryResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<LeaverCaseSummaryResponse>>> List(
        [FromQuery] string? q,
        [FromQuery] LeaverCaseStatusContract? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.SearchAsync(
            new LeaverCaseSearch(
                q,
                status.HasValue ? (LeaverCaseStatusView)status.Value : null,
                page,
                pageSize),
            cancellationToken);
        return Ok(
            new PagedResponse<LeaverCaseSummaryResponse>(
                result.Items.Select(item => item.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpGet("exceptions", Name = "ListLeaverCustodyExceptionsV1")]
    [Authorize(Policy = Permissions.LeaversRead)]
    [ProducesResponseType<PagedResponse<CustodyExceptionResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<CustodyExceptionResponse>>>
        ListExceptions(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
    {
        var result = await _service.SearchExceptionsAsync(
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<CustodyExceptionResponse>(
                result.Items.Select(item => item.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpGet("{leaverCaseId:guid}", Name = "GetLeaverCaseV1")]
    [Authorize(Policy = Permissions.LeaversRead)]
    [ProducesResponseType<LeaverCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LeaverCaseResponse>> Get(
        Guid leaverCaseId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(leaverCaseId, cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpGet("{leaverCaseId:guid}/assets", Name = "ListLeaverAssetsV1")]
    [Authorize(Policy = Permissions.LeaversRead)]
    [ProducesResponseType<PagedResponse<LeaverCustodyAssetResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<LeaverCustodyAssetResponse>>>
        ListAssets(
            Guid leaverCaseId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
    {
        var result = await _service.GetCurrentAssetsAsync(
            leaverCaseId,
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<LeaverCustodyAssetResponse>(
                result.Items.Select(item => item.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpPost(Name = "CreateLeaverCaseV1")]
    [Authorize(Policy = Permissions.LeaversManage)]
    [ProducesResponseType<LeaverCaseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaverCaseResponse>> Create(
        [FromBody] CreateLeaverCaseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(
            new CreateLeaverCaseCommand(
                request.PersonId,
                request.LeavingOn,
                request.Notes),
            cancellationToken);
        return CreatedAtAction(
            nameof(Get),
            new { leaverCaseId = result.Id },
            result.ToContract());
    }

    [HttpPut("{leaverCaseId:guid}", Name = "UpdateLeaverCaseV1")]
    [Authorize(Policy = Permissions.LeaversManage)]
    [ProducesResponseType<LeaverCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaverCaseResponse>> Update(
        Guid leaverCaseId,
        [FromBody] UpdateLeaverCaseRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(
            leaverCaseId,
            new UpdateLeaverCaseCommand(
                request.LeavingOn,
                request.Notes,
                request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost("{leaverCaseId:guid}/complete", Name = "CompleteLeaverCaseV1")]
    [Authorize(Policy = Permissions.LeaversManage)]
    [ProducesResponseType<LeaverCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaverCaseResponse>> Complete(
        Guid leaverCaseId,
        [FromBody] ChangeLeaverCaseStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CompleteAsync(
            leaverCaseId,
            new ChangeLeaverCaseStatusCommand(request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost("{leaverCaseId:guid}/cancel", Name = "CancelLeaverCaseV1")]
    [Authorize(Policy = Permissions.LeaversManage)]
    [ProducesResponseType<LeaverCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaverCaseResponse>> Cancel(
        Guid leaverCaseId,
        [FromBody] ChangeLeaverCaseStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CancelAsync(
            leaverCaseId,
            new ChangeLeaverCaseStatusCommand(request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost("{leaverCaseId:guid}/rehire", Name = "MarkLeaverRehiredV1")]
    [Authorize(Policy = Permissions.LeaversManage)]
    [ProducesResponseType<LeaverCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeaverCaseResponse>> MarkRehired(
        Guid leaverCaseId,
        [FromBody] ChangeLeaverCaseStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.MarkRehiredAsync(
            leaverCaseId,
            new ChangeLeaverCaseStatusCommand(request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }
}
