using Inventory.Api.Mapping;
using Inventory.Application.Attachments;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/assets/{assetId:guid}/attachments")]
[Produces("application/json")]
public sealed class AssetAttachmentsController : ControllerBase
{
    private const long MaximumMultipartRequestBytes =
        10L * 1024 * 1024 + 64 * 1024;

    private readonly AttachmentService _service;

    public AssetAttachmentsController(AttachmentService service)
    {
        _service = service;
    }

    [HttpGet(Name = "ListAssetAttachmentsV1")]
    [Authorize(Policy = Permissions.AttachmentsRead)]
    [ProducesResponseType<PagedResponse<AssetAttachmentResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<AssetAttachmentResponse>>> List(
        Guid assetId,
        [FromQuery] bool includeArchived = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.ListAsync(
            assetId,
            includeArchived,
            page,
            pageSize,
            cancellationToken);
        return Ok(
            new PagedResponse<AssetAttachmentResponse>(
                result.Items.Select(item => item.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpPost(Name = "UploadAssetAttachmentV1")]
    [Authorize(Policy = Permissions.AttachmentsManage)]
    [EnableRateLimiting("attachment-upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumMultipartRequestBytes)]
    [ProducesResponseType<AssetAttachmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<AssetAttachmentResponse>> Upload(
        Guid assetId,
        [FromForm] AttachmentCategoryContract category,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var content = file.OpenReadStream();
        var result = await _service.UploadAsync(
            assetId,
            new UploadAssetAttachmentCommand(
                category.ToApplication(),
                file.FileName,
                content),
            cancellationToken);
        return CreatedAtAction(
            nameof(Download),
            new
            {
                assetId,
                attachmentId = result.Id,
            },
            result.ToContract());
    }

    [HttpGet("{attachmentId:guid}/download", Name = "DownloadAssetAttachmentV1")]
    [Authorize(Policy = Permissions.AttachmentsRead)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Produces("application/pdf", "image/jpeg", "image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(
        Guid assetId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var result = await _service.DownloadAsync(
            assetId,
            attachmentId,
            cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["X-Content-SHA256"] = result.Sha256;
        return File(
            result.Content,
            result.ContentType,
            result.FileName,
            enableRangeProcessing: false);
    }

    [HttpPost("{attachmentId:guid}/archive", Name = "ArchiveAssetAttachmentV1")]
    [Authorize(Policy = Permissions.AttachmentsManage)]
    [ProducesResponseType<AssetAttachmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetAttachmentResponse>> Archive(
        Guid assetId,
        Guid attachmentId,
        [FromBody] ArchiveAssetAttachmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ArchiveAsync(
            assetId,
            attachmentId,
            new ArchiveAssetAttachmentCommand(request.Version),
            cancellationToken);
        return Ok(result.ToContract());
    }
}
