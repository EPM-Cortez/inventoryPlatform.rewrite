using Inventory.Api.Mapping;
using Inventory.Application.References;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/categories")]
[Produces("application/json")]
public sealed class CategoriesController : ControllerBase
{
    private readonly ReferenceDataService _referenceDataService;

    public CategoriesController(ReferenceDataService referenceDataService)
    {
        _referenceDataService = referenceDataService;
    }

    [HttpGet(Name = "ListCategoriesV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [ProducesResponseType<PagedResponse<CategoryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<CategoryResponse>>> List(
        [FromQuery] string? q,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _referenceDataService.ListCategoriesAsync(
            q,
            includeInactive,
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<CategoryResponse>(
                result.Items.Select(category => category.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpGet("{categoryId:guid}", Name = "GetCategoryV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> Get(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var category = await _referenceDataService.GetCategoryAsync(
            categoryId,
            cancellationToken);
        return Ok(category.ToContract());
    }

    [HttpPost(Name = "CreateCategoryV1")]
    [Authorize(Policy = Permissions.ReferenceManage)]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryResponse>> Create(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await _referenceDataService.CreateCategoryAsync(
            new CreateCategoryCommand(request.Code, request.Name, request.Description),
            cancellationToken);
        return CreatedAtAction(
            nameof(Get),
            new { categoryId = category.Id },
            category.ToContract());
    }

    [HttpPut("{categoryId:guid}", Name = "UpdateCategoryV1")]
    [Authorize(Policy = Permissions.ReferenceManage)]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryResponse>> Update(
        Guid categoryId,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await _referenceDataService.UpdateCategoryAsync(
            categoryId,
            new UpdateCategoryCommand(
                request.Code,
                request.Name,
                request.Description,
                request.IsActive,
                request.Version),
            cancellationToken);
        return Ok(category.ToContract());
    }
}
