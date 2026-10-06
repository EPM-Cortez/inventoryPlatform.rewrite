using Inventory.Api.Mapping;
using Inventory.Application.Auditing;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/audit")]
[Produces("application/json")]
public sealed class AuditController : ControllerBase
{
    private readonly AuditQueryService _auditQueryService;

    public AuditController(AuditQueryService auditQueryService)
    {
        _auditQueryService = auditQueryService;
    }

    [HttpGet(Name = "ListAuditLogsV1")]
    [Authorize(Policy = Permissions.AuditRead)]
    [ProducesResponseType<PagedResponse<AuditLogResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<AuditLogResponse>>> List(
        [FromQuery] string? action,
        [FromQuery] string? targetType,
        [FromQuery] string? targetId,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? outcome,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _auditQueryService.SearchAsync(
            CreateSearch(
                action,
                targetType,
                targetId,
                actorUserId,
                outcome,
                from,
                to,
                page,
                pageSize),
            cancellationToken);
        return Ok(
            new PagedResponse<AuditLogResponse>(
                result.Items.Select(audit => audit.ToContract()).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpGet("export", Name = "ExportAuditLogsV1")]
    [Authorize(Policy = Permissions.AuditExport)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export(
        [FromQuery] string? action,
        [FromQuery] string? targetType,
        [FromQuery] string? targetId,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? outcome,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var file = await _auditQueryService.ExportAsync(
            CreateSearch(
                action,
                targetType,
                targetId,
                actorUserId,
                outcome,
                from,
                to,
                page: 1,
                pageSize: 200),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Append(
            "X-Inventory-Row-Count",
            file.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Response.Headers.Append(
            "X-Inventory-Truncated",
            file.Truncated ? "true" : "false");
        return File(file.Content, "text/csv; charset=utf-8", file.FileName);
    }

    [HttpGet("vocabulary", Name = "GetAuditFilterVocabularyV1")]
    [Authorize(Policy = Permissions.AuditRead)]
    [ProducesResponseType<AuditFilterVocabularyResponse>(
        StatusCodes.Status200OK)]
    public async Task<ActionResult<AuditFilterVocabularyResponse>>
        GetFilterVocabulary(
            CancellationToken cancellationToken)
    {
        var result = await _auditQueryService.GetFilterVocabularyAsync(
            cancellationToken);
        return Ok(
            new AuditFilterVocabularyResponse(
                result.Actions,
                result.TargetTypes,
                result.Outcomes,
                result.Truncated));
    }

    private static AuditLogSearch CreateSearch(
        string? action,
        string? targetType,
        string? targetId,
        Guid? actorUserId,
        string? outcome,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize) =>
        new(
            action,
            targetType,
            targetId,
            actorUserId,
            outcome,
            from,
            to,
            page,
            pageSize);
}
