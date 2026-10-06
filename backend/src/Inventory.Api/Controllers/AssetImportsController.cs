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
public sealed class AssetImportsController : ControllerBase
{
    private readonly ImportExportService _service;

    public AssetImportsController(ImportExportService service)
    {
        _service = service;
    }

    [HttpPost("assets/validate", Name = "ValidateAssetImportV1")]
    [Authorize(Policy = Permissions.ImportsValidate)]
    [EnableRateLimiting("import-preview")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType<AssetImportJobResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetImportJobResponse>> ValidateAssets(
        [FromForm] ValidateAssetImportForm form,
        CancellationToken cancellationToken)
    {
        await using var content = form.File.OpenReadStream();
        var job = await _service.ValidateAssetImportAsync(
            new AssetImportFile(
                form.File.FileName,
                form.File.ContentType,
                form.File.Length,
                content),
            form.Mode!.Value.ToDomain(),
            cancellationToken);
        return CreatedAtAction(
            nameof(Get),
            new { importJobId = job.Id },
            job.ToContract());
    }

    [HttpGet("{importJobId:guid}", Name = "GetAssetImportJobV1")]
    [Authorize(Policy = Permissions.ImportsRead)]
    [Produces("application/json")]
    [ProducesResponseType<AssetImportJobResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetImportJobResponse>> Get(
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        var job = await _service.GetImportJobAsync(importJobId, cancellationToken);
        return Ok(job.ToContract());
    }

    [HttpPost("{importJobId:guid}/commit", Name = "CommitAssetImportV1")]
    [Authorize(Policy = Permissions.ImportsCommit)]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType<AssetImportCommitResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetImportCommitResponse>> Commit(
        Guid importJobId,
        [FromBody] CommitAssetImportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CommitAssetImportAsync(
            importJobId,
            request.Version,
            cancellationToken);
        return Ok(result.ToContract());
    }

    [HttpGet("{importJobId:guid}/errors.csv", Name = "DownloadAssetImportErrorsV1")]
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
}

public sealed class ValidateAssetImportForm
{
    [Required]
    public IFormFile File { get; init; } = null!;

    [Required]
    public AssetImportModeContract? Mode { get; init; }
}
