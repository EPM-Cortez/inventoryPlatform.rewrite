using Inventory.Api.Mapping;
using Inventory.Application.People;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/people")]
[Produces("application/json")]
public sealed class PeopleController : ControllerBase
{
    private readonly PeopleService _peopleService;
    private readonly IAuthorizationService _authorizationService;

    public PeopleController(
        PeopleService peopleService,
        IAuthorizationService authorizationService)
    {
        _peopleService = peopleService;
        _authorizationService = authorizationService;
    }

    [HttpGet(Name = "ListPeopleV1")]
    [Authorize(Policy = Permissions.PeopleRead)]
    [ProducesResponseType<PagedResponse<PersonResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<PersonResponse>>> List(
        [FromQuery] string? q,
        [FromQuery] PersonTypeContract? type,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string sort = "displayName",
        CancellationToken cancellationToken = default)
    {
        var result = await _peopleService.SearchAsync(
            new PersonSearch(
                q,
                type?.ToDomain(),
                includeInactive,
                page,
                pageSize,
                sort),
            cancellationToken);
        return Ok(
            new PagedResponse<PersonResponse>(
                result.Items.Select(person => person.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpGet("{personId:guid}", Name = "GetPersonV1")]
    [Authorize(Policy = Permissions.PeopleRead)]
    [ProducesResponseType<PersonResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonResponse>> Get(
        Guid personId,
        CancellationToken cancellationToken)
    {
        var person = await _peopleService.GetAsync(personId, cancellationToken);
        return Ok(person.ToContract());
    }

    [HttpPost(Name = "CreatePersonV1")]
    [Authorize(Policy = Permissions.PeopleOperate)]
    [ProducesResponseType<PersonResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonResponse>> Create(
        [FromBody] CreatePersonRequest request,
        CancellationToken cancellationToken)
    {
        var person = await _peopleService.CreateAsync(
            new CreatePersonCommand(
                request.ExternalId,
                request.DisplayName,
                request.Type.ToDomain(),
                request.Email),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { personId = person.Id }, person.ToContract());
    }

    [HttpPut("{personId:guid}", Name = "UpdatePersonV1")]
    [Authorize(Policy = Permissions.PeopleOperate)]
    [ProducesResponseType<PersonResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonResponse>> Update(
        Guid personId,
        [FromBody] UpdatePersonRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.IsActive)
        {
            var archiveAuthorization = await _authorizationService.AuthorizeAsync(
                User,
                resource: null,
                Permissions.PeopleManage);
            if (!archiveAuthorization.Succeeded)
            {
                return Forbid();
            }
        }

        var person = await _peopleService.UpdateAsync(
            personId,
            new UpdatePersonCommand(
                request.ExternalId,
                request.DisplayName,
                request.Type.ToDomain(),
                request.Email,
                request.IsActive,
                request.Version),
            cancellationToken);
        return Ok(person.ToContract());
    }

    [HttpGet("{personId:guid}/assets", Name = "ListPersonAssetsV1")]
    [Authorize(Policy = Permissions.AssetsRead)]
    [ProducesResponseType<PagedResponse<HolderAssetResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<HolderAssetResponse>>> ListAssets(
        Guid personId,
        [FromQuery] bool includeHistory = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _peopleService.GetAssetsAsync(
            personId,
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
