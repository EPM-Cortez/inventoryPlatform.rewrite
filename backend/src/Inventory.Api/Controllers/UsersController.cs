using System.ComponentModel.DataAnnotations;
using Inventory.Api.Mapping;
using Inventory.Application.Identity;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
[Authorize(Policy = Permissions.UsersManage)]
public sealed class UsersController : ControllerBase
{
    private readonly IIdentityService _identityService;

    public UsersController(IIdentityService identityService)
    {
        _identityService = identityService;
    }

    [HttpGet(Name = "ListUsersV1")]
    [ProducesResponseType<PagedResponse<UserResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<UserResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var users = await _identityService.ListUsersAsync(page, pageSize, cancellationToken);
        return Ok(
            new PagedResponse<UserResponse>(
                users.Items.Select(user => user.ToContract()).ToArray(),
                users.Page,
                users.PageSize,
                users.TotalCount));
    }

    [HttpGet("role-presets", Name = "ListUserRolePresetsV1")]
    [ProducesResponseType<IReadOnlyList<RolePresetResponse>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<RolePresetResponse>> ListRolePresets()
    {
        var presets = RolePermissionPresets.All
            .OrderBy(preset => preset.Key, StringComparer.Ordinal)
            .Select(
                preset => new RolePresetResponse(
                    preset.Key,
                    preset.Value.Order(StringComparer.Ordinal).ToArray()))
            .ToArray();
        return Ok(presets);
    }

    [HttpGet("{userId:guid}", Name = "GetUserV1")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> Get(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await _identityService.GetUserAsync(userId, cancellationToken);
        return Ok(user.ToContract());
    }

    [HttpPost(Name = "CreateUserV1")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _identityService.CreateUserAsync(
            new CreateUserCommand(
                request.Email,
                request.DisplayName,
                request.TemporaryPassword,
                request.Roles),
            cancellationToken);
        return CreatedAtRoute(
            "GetUserV1",
            new { userId = user.Id },
            user.ToContract());
    }

    [HttpPut("{userId:guid}", Name = "UpdateUserV1")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Update(
        Guid userId,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _identityService.UpdateUserAsync(
            userId,
            new UpdateUserCommand(
                request.Email,
                request.DisplayName,
                request.IsEnabled,
                request.Roles,
                request.Version ?? Guid.Empty),
            cancellationToken);
        return Ok(user.ToContract());
    }

    [HttpPut("{userId:guid}/roles", Name = "SetUserRolesV1")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> SetRoles(
        Guid userId,
        [FromBody] SetUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _identityService.SetRolesAsync(
            userId,
            request.Roles,
            request.Version ?? Guid.Empty,
            cancellationToken);
        return Ok(user.ToContract());
    }

    [HttpPost("{userId:guid}/disable", Name = "DisableUserV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Disable(
        Guid userId,
        [FromBody] UserVersionRequest request,
        CancellationToken cancellationToken)
    {
        await _identityService.DisableUserAsync(
            userId,
            request.Version ?? Guid.Empty,
            cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}", Name = "ArchiveUserV1")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Archive(
        Guid userId,
        [FromQuery, Required] Guid? version,
        CancellationToken cancellationToken)
    {
        await _identityService.ArchiveUserAsync(
            userId,
            version ?? Guid.Empty,
            cancellationToken);
        return NoContent();
    }

    [HttpPost("{userId:guid}/restore", Name = "RestoreUserV1")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Restore(
        Guid userId,
        [FromBody] UserVersionRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _identityService.RestoreUserAsync(
            userId,
            request.Version ?? Guid.Empty,
            cancellationToken);
        return Ok(user.ToContract());
    }

    [HttpPost("{userId:guid}/reset-password", Name = "ResetUserPasswordV1")]
    [EnableRateLimiting("administrator-password-reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResetPassword(
        Guid userId,
        [FromBody] ResetUserPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _identityService.ResetPasswordAsync(
            userId,
            request.NewTemporaryPassword,
            request.Version ?? Guid.Empty,
            cancellationToken);
        return NoContent();
    }

    [HttpPost("{userId:guid}/reset-mfa", Name = "ResetUserMfaV1")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> ResetMfa(
        Guid userId,
        [FromBody] UserVersionRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _identityService.ResetMfaAsync(
            userId,
            request.Version ?? Guid.Empty,
            cancellationToken);
        return Ok(user.ToContract());
    }
}
