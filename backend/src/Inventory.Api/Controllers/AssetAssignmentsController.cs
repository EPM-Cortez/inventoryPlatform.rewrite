using Inventory.Api.Mapping;
using Inventory.Application.Assignments;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/assets/{assetId:guid}/assignments")]
[Produces("application/json")]
public sealed class AssetAssignmentsController : ControllerBase
{
    private readonly AssignmentService _assignmentService;

    public AssetAssignmentsController(AssignmentService assignmentService)
    {
        _assignmentService = assignmentService;
    }

    [HttpGet(Name = "ListAssetAssignmentsV1")]
    [Authorize(Policy = Permissions.AssignmentsRead)]
    [ProducesResponseType<PagedResponse<AssetAssignmentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<AssetAssignmentResponse>>> List(
        Guid assetId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _assignmentService.GetForAssetAsync(
            assetId,
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<AssetAssignmentResponse>(
                result.Items.Select(assignment => assignment.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpPost(Name = "AssignOrTransferAssetV1")]
    [Authorize(Policy = Permissions.AssignmentsManage)]
    [ProducesResponseType<AssignmentWorkflowResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssignmentWorkflowResponse>> AssignOrTransfer(
        Guid assetId,
        [FromBody] AssignAssetRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _assignmentService.AssignOrTransferAsync(
            assetId,
            new AssignAssetCommand(
                request.PersonId,
                request.DepartmentId,
                request.AssetVersion,
                request.Notes,
                request.ExpectedReturnOn),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPut(
        "{assignmentId:guid}/expected-return",
        Name = "UpdateAssetAssignmentExpectedReturnV1")]
    [Authorize(Policy = Permissions.AssignmentsManage)]
    [ProducesResponseType<AssetAssignmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetAssignmentResponse>> UpdateExpectedReturn(
        Guid assetId,
        Guid assignmentId,
        [FromBody] UpdateExpectedReturnRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _assignmentService.UpdateExpectedReturnAsync(
            assetId,
            assignmentId,
            new UpdateExpectedReturnCommand(
                request.ExpectedReturnOn,
                request.AssignmentVersion),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost("{assignmentId:guid}/return", Name = "ReturnAssetAssignmentV1")]
    [Authorize(Policy = Permissions.AssignmentsManage)]
    [ProducesResponseType<AssignmentWorkflowResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssignmentWorkflowResponse>> Return(
        Guid assetId,
        Guid assignmentId,
        [FromBody] ReturnAssetRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _assignmentService.ReturnAsync(
            assetId,
            assignmentId,
            new ReturnAssetCommand(
                request.AssetVersion,
                request.AssignmentVersion,
                request.Condition?.ToDomain(),
                request.LocationId,
                request.Notes),
            cancellationToken);
        return Ok(result.ToContract());
    }
}
