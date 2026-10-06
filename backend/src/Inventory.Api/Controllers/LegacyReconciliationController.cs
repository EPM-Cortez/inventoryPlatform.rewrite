using System.ComponentModel.DataAnnotations;
using Inventory.Api.Mapping;
using Inventory.Application.DataExchange;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/legacy-reconciliation")]
[Produces("application/json")]
[Authorize(Policy = Permissions.LegacyReconcile)]
public sealed class LegacyReconciliationController : ControllerBase
{
    private readonly LegacyReconciliationService _service;

    public LegacyReconciliationController(
        LegacyReconciliationService service)
    {
        _service = service;
    }

    [HttpPost("dry-runs", Name = "CreateLegacyReconciliationDryRunV1")]
    [EnableRateLimiting("import-preview")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<LegacyReconciliationResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LegacyReconciliationResponse>> CreateDryRun(
        [FromForm] CreateLegacyReconciliationDryRunForm form,
        CancellationToken cancellationToken)
    {
        await using var content = form.File.OpenReadStream();
        var result = await _service.CreateDryRunAsync(
            new AssetImportFile(
                form.File.FileName,
                form.File.ContentType,
                form.File.Length,
                content),
            form.Mode!.Value.ToDomain(),
            cancellationToken);
        return CreatedAtAction(
            nameof(Get),
            new { importJobId = result.Report.ImportJobId },
            result.ToContract());
    }

    [HttpGet("{importJobId:guid}", Name = "GetLegacyReconciliationV1")]
    [ProducesResponseType<LegacyReconciliationResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LegacyReconciliationResponse>> Get(
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(
            importJobId,
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpPost(
        "{importJobId:guid}/sign-off",
        Name = "SignOffLegacyReconciliationV1")]
    [Consumes("application/json")]
    [ProducesResponseType<LegacyReconciliationResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LegacyReconciliationResponse>> SignOff(
        Guid importJobId,
        [FromBody] SignOffLegacyReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.SignOffAsync(
            importJobId,
            new LegacyReconciliationSignOffCommand(
                request.Decision.ToDomain(),
                request.ReportFingerprint,
                request.Notes,
                request.Exceptions
                    .Select(
                        exception =>
                            new LegacyExceptionAcknowledgement(
                                exception.IssueKey,
                                exception.Rationale))
                    .ToArray()),
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpGet(
        "{importJobId:guid}/cutover-package",
        Name = "GetLegacyCutoverPackageV1")]
    [ProducesResponseType<LegacyCutoverPackageResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LegacyCutoverPackageResponse>>
        GetCutoverPackage(
            Guid importJobId,
            CancellationToken cancellationToken)
    {
        var package = await _service.GetCutoverPackageAsync(
            importJobId,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ContentDisposition =
            $"attachment; filename=\"legacy-cutover-{importJobId:N}.json\"";
        return Ok(package.ToContract());
    }
}

public sealed class CreateLegacyReconciliationDryRunForm
{
    [Required]
    public IFormFile File { get; init; } = null!;

    [Required]
    public AssetImportModeContract? Mode { get; init; }
}
