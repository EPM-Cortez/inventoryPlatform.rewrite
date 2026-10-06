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
[Route("api/v1/imports")]
public sealed class ReferenceDataImportsController : ControllerBase
{
    private readonly ReferenceDataExchangeService _service;

    public ReferenceDataImportsController(ReferenceDataExchangeService service)
    {
        _service = service;
    }

    [HttpPost("people/validate", Name = "ValidatePeopleImportV1")]
    [Authorize(Policy = Permissions.ImportsValidate)]
    [EnableRateLimiting("import-preview")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType<ReferenceDataImportJobResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReferenceDataImportJobResponse>> ValidatePeople(
        [FromForm] ValidateReferenceDataImportForm form,
        CancellationToken cancellationToken)
    {
        await using var content = form.File.OpenReadStream();
        var job = await _service.ValidatePeopleImportAsync(
            ToImportFile(form.File, content),
            form.Mode!.Value.ToDomain(),
            cancellationToken);
        return CreatedAtAction(
            nameof(Get),
            new { importJobId = job.Id },
            job.ToContract());
    }

    [HttpPost("departments/validate", Name = "ValidateDepartmentsImportV1")]
    [Authorize(Policy = Permissions.ImportsValidate)]
    [EnableRateLimiting("import-preview")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType<ReferenceDataImportJobResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReferenceDataImportJobResponse>> ValidateDepartments(
        [FromForm] ValidateReferenceDataImportForm form,
        CancellationToken cancellationToken)
    {
        await using var content = form.File.OpenReadStream();
        var job = await _service.ValidateDepartmentImportAsync(
            ToImportFile(form.File, content),
            form.Mode!.Value.ToDomain(),
            cancellationToken);
        return CreatedAtAction(
            nameof(Get),
            new { importJobId = job.Id },
            job.ToContract());
    }

    [HttpPost("locations/validate", Name = "ValidateLocationsImportV1")]
    [Authorize(Policy = Permissions.ImportsValidate)]
    [EnableRateLimiting("import-preview")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType<ReferenceDataImportJobResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReferenceDataImportJobResponse>> ValidateLocations(
        [FromForm] ValidateReferenceDataImportForm form,
        CancellationToken cancellationToken)
    {
        await using var content = form.File.OpenReadStream();
        var job = await _service.ValidateLocationImportAsync(
            ToImportFile(form.File, content),
            form.Mode!.Value.ToDomain(),
            cancellationToken);
        return CreatedAtAction(
            nameof(Get),
            new { importJobId = job.Id },
            job.ToContract());
    }

    [HttpGet(
        "reference-data/{importJobId:guid}",
        Name = "GetReferenceDataImportJobV1")]
    [Authorize(Policy = Permissions.ImportsRead)]
    [Produces("application/json")]
    [ProducesResponseType<ReferenceDataImportJobResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReferenceDataImportJobResponse>> Get(
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        var job = await _service.GetImportJobAsync(importJobId, cancellationToken);
        return Ok(job.ToContract());
    }

    [HttpPost(
        "reference-data/{importJobId:guid}/commit",
        Name = "CommitReferenceDataImportV1")]
    [Authorize(Policy = Permissions.ImportsCommit)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType<ReferenceDataImportCommitResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReferenceDataImportCommitResponse>> Commit(
        Guid importJobId,
        [FromBody] CommitReferenceDataImportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CommitImportAsync(
            importJobId,
            request.Version,
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpGet(
        "reference-data/{importJobId:guid}/errors.csv",
        Name = "DownloadReferenceDataImportErrorsV1")]
    [Authorize(Policy = Permissions.ImportsRead)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadErrors(
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        var file = await _service.GetImportErrorsCsvAsync(
            importJobId,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Append(
            "X-Inventory-Row-Count",
            file.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return File(file.Content, "text/csv; charset=utf-8", file.FileName);
    }

    private static ReferenceDataImportFile ToImportFile(
        IFormFile file,
        Stream content) =>
        new(file.FileName, file.ContentType, file.Length, content);
}

public sealed class ValidateReferenceDataImportForm
{
    [Required]
    public IFormFile File { get; init; } = null!;

    [Required]
    public ReferenceDataImportModeContract? Mode { get; init; }
}
