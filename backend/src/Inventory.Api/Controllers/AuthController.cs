using Inventory.Application.Common;
using Inventory.Application.Identity;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IIdentityService _identityService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAntiforgery _antiforgery;

    public AuthController(
        IIdentityService identityService,
        ICurrentUserContext currentUser,
        IAntiforgery antiforgery)
    {
        _identityService = identityService;
        _currentUser = currentUser;
        _antiforgery = antiforgery;
    }

    [HttpGet("csrf", Name = "GetCsrfTokenV1")]
    [AllowAnonymous]
    [ProducesResponseType<CsrfTokenResponse>(StatusCodes.Status200OK)]
    public ActionResult<CsrfTokenResponse> GetCsrfToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(
            new CsrfTokenResponse(
                tokens.RequestToken
                ?? throw new InvalidOperationException("The antiforgery token could not be issued.")));
    }

    [HttpPost("login", Name = "LoginV1")]
    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _identityService.LoginAsync(
            request.Email,
            request.Password,
            request.RememberMe,
            cancellationToken);
        if (result.Succeeded)
        {
            return NoContent();
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Type = "https://inventory.local/problems/authentication-failed",
            Title = result.FailureCode == "mfa_required"
                ? "Multi-factor authentication is required."
                : "The email address or password was not accepted.",
            Detail = result.FailureCode == "mfa_required"
                ? "Complete the multi-factor authentication challenge."
                : "Check the credentials and try again.",
            Instance = Request.Path,
        };
        problem.Extensions["code"] =
            result.FailureCode == "mfa_required" ? "mfa_required" : "invalid_credentials";
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return Unauthorized(problem);
    }

    [HttpPost("login/mfa", Name = "CompleteMfaLoginV1")]
    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CompleteMfaLogin(
        [FromBody] MfaLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _identityService.CompleteMfaLoginAsync(
            request.Code,
            request.RememberMe,
            request.RememberMachine,
            cancellationToken);
        if (result.Succeeded)
        {
            return NoContent();
        }

        return Unauthorized(
            new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Type = "https://inventory.local/problems/authentication-failed",
                Title = result.FailureCode == "locked_out"
                    ? "The account is temporarily locked."
                    : "The multi-factor authentication code was not accepted.",
                Detail = result.FailureCode == "locked_out"
                    ? "Try again later."
                    : "Check the code and try again.",
                Instance = Request.Path,
                Extensions =
                {
                    ["code"] = result.FailureCode == "locked_out"
                        ? "locked_out"
                        : "invalid_mfa_code",
                    ["traceId"] = HttpContext.TraceIdentifier,
                },
            });
    }

    [HttpPost("login/mfa/recovery", Name = "CompleteMfaRecoveryLoginV1")]
    [AllowAnonymous]
    [EnableRateLimiting("authentication")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CompleteMfaRecoveryLogin(
        [FromBody] MfaRecoveryLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _identityService.CompleteMfaRecoveryLoginAsync(
            request.RecoveryCode,
            request.RememberMe,
            cancellationToken);
        if (result.Succeeded)
        {
            return NoContent();
        }

        return Unauthorized(
            new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Type = "https://inventory.local/problems/authentication-failed",
                Title = result.FailureCode == "locked_out"
                    ? "The account is temporarily locked."
                    : "The MFA recovery code was not accepted.",
                Detail = result.FailureCode == "locked_out"
                    ? "Try again later."
                    : "Check the recovery code and try again.",
                Instance = Request.Path,
                Extensions =
                {
                    ["code"] = result.FailureCode == "locked_out"
                        ? "locked_out"
                        : "invalid_mfa_recovery_code",
                    ["traceId"] = HttpContext.TraceIdentifier,
                },
            });
    }

    [HttpPost("logout", Name = "LogoutV1")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _identityService.LogoutAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("me", Name = "GetCurrentUserV1")]
    [Authorize]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    public ActionResult<CurrentUserResponse> Me()
    {
        var actor = _currentUser.RequireActor();
        var mustChangePassword = string.Equals(
            User.FindFirst(Permissions.MustChangePasswordClaimType)?.Value,
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);
        var mfaEnrollmentRequired = string.Equals(
            User.FindFirst(Permissions.MfaEnrollmentRequiredClaimType)?.Value,
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);

        return Ok(
            new CurrentUserResponse(
                actor.UserId,
                actor.DisplayName,
                _currentUser.Roles.Order(StringComparer.Ordinal).ToArray(),
                _currentUser.Permissions.Order(StringComparer.Ordinal).ToArray(),
                mustChangePassword,
                mfaEnrollmentRequired));
    }

    [HttpPost("change-password", Name = "ChangeOwnPasswordV1")]
    [Authorize]
    [EnableRateLimiting("self-service-password-change")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        await _identityService.ChangePasswordAsync(
            actor.UserId,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);
        return NoContent();
    }

    [HttpGet("mfa", Name = "GetOwnMfaStatusV1")]
    [Authorize]
    [ProducesResponseType<MfaStatusResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MfaStatusResponse>> GetMfaStatus(
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        var status = await _identityService.GetMfaStatusAsync(
            actor.UserId,
            cancellationToken);
        return Ok(
            new MfaStatusResponse(
                status.Enabled,
                status.EnrollmentRequired,
                status.RecoveryCodesRemaining));
    }

    [HttpPost("mfa/enrollment", Name = "BeginOwnMfaEnrollmentV1")]
    [Authorize]
    [EnableRateLimiting("mfa-self-service")]
    [ProducesResponseType<MfaEnrollmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MfaEnrollmentResponse>> BeginMfaEnrollment(
        [FromBody] BeginMfaEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        var enrollment = await _identityService.BeginMfaEnrollmentAsync(
            actor.UserId,
            request.CurrentPassword,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(
            new MfaEnrollmentResponse(
                enrollment.SharedKey,
                enrollment.AuthenticatorUri));
    }

    [HttpPost("mfa/enrollment/confirm", Name = "ConfirmOwnMfaEnrollmentV1")]
    [Authorize]
    [EnableRateLimiting("mfa-self-service")]
    [ProducesResponseType<MfaRecoveryCodesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MfaRecoveryCodesResponse>> ConfirmMfaEnrollment(
        [FromBody] ConfirmMfaEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        var result = await _identityService.ConfirmMfaEnrollmentAsync(
            actor.UserId,
            request.Code,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(
            new MfaRecoveryCodesResponse(
                result.RecoveryCodes,
                result.RecoveryCodesRemaining));
    }

    [HttpPost("mfa/recovery-codes", Name = "RegenerateOwnMfaRecoveryCodesV1")]
    [Authorize]
    [EnableRateLimiting("mfa-self-service")]
    [ProducesResponseType<MfaRecoveryCodesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MfaRecoveryCodesResponse>> RegenerateMfaRecoveryCodes(
        [FromBody] RegenerateMfaRecoveryCodesRequest request,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        var result = await _identityService.RegenerateMfaRecoveryCodesAsync(
            actor.UserId,
            request.CurrentPassword,
            request.Code,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(
            new MfaRecoveryCodesResponse(
                result.RecoveryCodes,
                result.RecoveryCodesRemaining));
    }
}
