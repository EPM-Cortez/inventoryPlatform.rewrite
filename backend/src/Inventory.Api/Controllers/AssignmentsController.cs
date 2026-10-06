using Inventory.Api.Mapping;
using Inventory.Application.Assignments;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/assignments")]
[Produces("application/json")]
public sealed class AssignmentsController : ControllerBase
{
    private readonly AssignmentService _assignmentService;

    public AssignmentsController(AssignmentService assignmentService)
    {
        _assignmentService = assignmentService;
    }

    [HttpGet("overdue", Name = "ListOverdueAssignmentsV1")]
    [Authorize(Policy = Permissions.AssignmentsRead)]
    [ProducesResponseType<PagedResponse<OverdueLoanResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<OverdueLoanResponse>>> ListOverdue(
        [FromQuery] Guid? personId,
        [FromQuery] Guid? departmentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _assignmentService.SearchOverdueAsync(
            personId,
            departmentId,
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<OverdueLoanResponse>(
                result.Items.Select(item => item.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }
}
