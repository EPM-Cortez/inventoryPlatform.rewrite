using Inventory.Api.Mapping;
using Inventory.Application.People;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/departments")]
[Produces("application/json")]
public sealed class DepartmentsController : ControllerBase
{
    private readonly DepartmentService _departmentService;

    public DepartmentsController(DepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    [HttpGet(Name = "ListDepartmentsV1")]
    [Authorize(Policy = Permissions.PeopleRead)]
    [ProducesResponseType<PagedResponse<DepartmentResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<DepartmentResponse>>> List(
        [FromQuery] string? q,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string sort = "name",
        CancellationToken cancellationToken = default)
    {
        var result = await _departmentService.SearchAsync(
            new DepartmentSearch(q, includeInactive, page, pageSize, sort),
            cancellationToken);
        return Ok(
            new PagedResponse<DepartmentResponse>(
                result.Items.Select(department => department.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpGet("{departmentId:guid}", Name = "GetDepartmentV1")]
    [Authorize(Policy = Permissions.PeopleRead)]
    [ProducesResponseType<DepartmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentResponse>> Get(
        Guid departmentId,
        CancellationToken cancellationToken)
    {
        var department = await _departmentService.GetAsync(
            departmentId,
            cancellationToken);
        return Ok(department.ToContract());
    }

    [HttpPost(Name = "CreateDepartmentV1")]
    [Authorize(Policy = Permissions.PeopleManage)]
    [ProducesResponseType<DepartmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentResponse>> Create(
        [FromBody] CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var department = await _departmentService.CreateAsync(
            new CreateDepartmentCommand(request.Code, request.Name),
            cancellationToken);
        return CreatedAtAction(
            nameof(Get),
            new { departmentId = department.Id },
            department.ToContract());
    }

    [HttpPut("{departmentId:guid}", Name = "UpdateDepartmentV1")]
    [Authorize(Policy = Permissions.PeopleManage)]
    [ProducesResponseType<DepartmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentResponse>> Update(
        Guid departmentId,
        [FromBody] UpdateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var department = await _departmentService.UpdateAsync(
            departmentId,
            new UpdateDepartmentCommand(
                request.Code,
                request.Name,
                request.IsActive,
                request.Version),
            cancellationToken);
        return Ok(department.ToContract());
    }

    [HttpGet("{departmentId:guid}/assets", Name = "ListDepartmentAssetsV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [ProducesResponseType<PagedResponse<HolderAssetResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<HolderAssetResponse>>> ListAssets(
        Guid departmentId,
        [FromQuery] bool includeHistory = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _departmentService.GetAssetsAsync(
            departmentId,
            includeHistory,
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<HolderAssetResponse>(
                result.Items.Select(asset => asset.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }
}
