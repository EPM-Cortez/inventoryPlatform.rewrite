using Inventory.Api.Mapping;
using Inventory.Application.Common;
using Inventory.Application.Security;
using Inventory.Application.Stocktakes;
using Inventory.Contracts.V1;
using Inventory.Domain.Stocktakes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/stocktakes")]
[Produces("application/json")]
public sealed class StocktakesController : ControllerBase
{
    private readonly StocktakeService _stocktakeService;

    public StocktakesController(StocktakeService stocktakeService)
    {
        _stocktakeService = stocktakeService;
    }

    [HttpGet(Name = "ListStocktakesV1")]
    [Authorize(Policy = Permissions.StocktakesRead)]
    [ProducesResponseType<PagedResponse<StocktakeSessionListItemResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<StocktakeSessionListItemResponse>>> List(
        [FromQuery] StocktakeStatusContract? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _stocktakeService.SearchAsync(
            new StocktakeSessionSearch(
                status.HasValue ? ToDomain(status.Value) : null,
                page,
                pageSize),
            cancellationToken);
        return Ok(
            new PagedResponse<StocktakeSessionListItemResponse>(
                result.Items.Select(ToContract).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpGet("{sessionId:guid}", Name = "GetStocktakeV1")]
    [Authorize(Policy = Permissions.StocktakesRead)]
    [ProducesResponseType<StocktakeSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StocktakeSessionResponse>> Get(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await _stocktakeService.GetAsync(sessionId, cancellationToken);
        return Ok(ToContract(session));
    }

    [HttpPost(Name = "StartStocktakeV1")]
    [Authorize(Policy = Permissions.StocktakesManage)]
    [EnableRateLimiting("stocktake-start")]
    [ProducesResponseType<StocktakeSessionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StocktakeSessionResponse>> Start(
        [FromBody] StartStocktakeRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _stocktakeService.StartAsync(
            new StartStocktakeCommand(
                request.Name,
                ToDomain(request.Scope),
                request.LocationId,
                request.CategoryId),
            cancellationToken);
        return CreatedAtAction(
            nameof(Get),
            new { sessionId = session.Id },
            ToContract(session));
    }

    [HttpGet("{sessionId:guid}/items", Name = "ListStocktakeItemsV1")]
    [Authorize(Policy = Permissions.StocktakesRead)]
    [ProducesResponseType<PagedResponse<StocktakeItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<StocktakeItemResponse>>> Items(
        Guid sessionId,
        [FromQuery] StocktakeItemFilterContract filter = StocktakeItemFilterContract.All,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _stocktakeService.GetItemsAsync(
            sessionId,
            new StocktakeItemSearch(ToDomain(filter), page, pageSize),
            cancellationToken);
        return Ok(
            new PagedResponse<StocktakeItemResponse>(
                result.Items.Select(ToContract).ToArray(),
                result.Page,
                result.PageSize,
                result.TotalCount));
    }

    [HttpPost("{sessionId:guid}/observations", Name = "RecordStocktakeObservationV1")]
    [Authorize(Policy = Permissions.StocktakesManage)]
    [ProducesResponseType<StocktakeObservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StocktakeObservationResponse>> Observe(
        Guid sessionId,
        [FromBody] RecordStocktakeObservationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _stocktakeService.RecordObservationAsync(
            sessionId,
            new RecordStocktakeObservationCommand(
                request.AssetId,
                request.AssetTag,
                request.ObservedLocationId,
                request.ObservedCondition.ToDomain(),
                request.Note,
                request.Version),
            cancellationToken);
        return Ok(
            new StocktakeObservationResponse(
                ToContract(result.Session),
                ToContract(result.Item),
                result.Changed));
    }

    [HttpPost("{sessionId:guid}/complete", Name = "CompleteStocktakeV1")]
    [Authorize(Policy = Permissions.StocktakesManage)]
    [ProducesResponseType<StocktakeSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StocktakeSessionResponse>> Complete(
        Guid sessionId,
        [FromBody] StocktakeActionRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _stocktakeService.CompleteAsync(
            sessionId,
            request.Version,
            cancellationToken);
        return Ok(ToContract(session));
    }

    [HttpPost("{sessionId:guid}/cancel", Name = "CancelStocktakeV1")]
    [Authorize(Policy = Permissions.StocktakesManage)]
    [ProducesResponseType<StocktakeSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StocktakeSessionResponse>> Cancel(
        Guid sessionId,
        [FromBody] CancelStocktakeRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _stocktakeService.CancelAsync(
            sessionId,
            request.Version,
            request.Reason,
            cancellationToken);
        return Ok(ToContract(session));
    }

    private static StocktakeScope ToDomain(StocktakeScopeContract scope) =>
        scope switch
        {
            StocktakeScopeContract.All => StocktakeScope.All,
            StocktakeScopeContract.Location => StocktakeScope.Location,
            StocktakeScopeContract.Category => StocktakeScope.Category,
            _ => throw new RequestValidationException(
                "invalid_stocktake_scope",
                "The stocktake scope is not supported."),
        };

    private static StocktakeStatus ToDomain(StocktakeStatusContract status) =>
        status switch
        {
            StocktakeStatusContract.InProgress => StocktakeStatus.InProgress,
            StocktakeStatusContract.Completed => StocktakeStatus.Completed,
            StocktakeStatusContract.Cancelled => StocktakeStatus.Cancelled,
            _ => throw new RequestValidationException(
                "invalid_stocktake_status",
                "The stocktake status is not supported."),
        };

    private static StocktakeItemFilter ToDomain(StocktakeItemFilterContract filter) =>
        filter switch
        {
            StocktakeItemFilterContract.All => StocktakeItemFilter.All,
            StocktakeItemFilterContract.Seen => StocktakeItemFilter.Seen,
            StocktakeItemFilterContract.Missing => StocktakeItemFilter.Missing,
            StocktakeItemFilterContract.Unexpected => StocktakeItemFilter.Unexpected,
            StocktakeItemFilterContract.WrongLocation => StocktakeItemFilter.WrongLocation,
            StocktakeItemFilterContract.ConditionChanged => StocktakeItemFilter.ConditionChanged,
            _ => throw new RequestValidationException(
                "invalid_stocktake_item_filter",
                "The stocktake item filter is not supported."),
        };

    private static StocktakeSessionResponse ToContract(StocktakeSessionView session) =>
        new(
            session.Id,
            session.Name,
            ToContract(session.Scope),
            session.ScopeLocation is null ? null : ToContract(session.ScopeLocation),
            session.ScopeCategory is null ? null : ToContract(session.ScopeCategory),
            ToContract(session.Status),
            session.StartedAtUtc,
            session.StartedByUserId,
            session.StartedByDisplayName,
            session.CompletedAtUtc,
            session.CompletedByUserId,
            session.CompletedByDisplayName,
            session.CancelledAtUtc,
            session.CancelledByUserId,
            session.CancelledByDisplayName,
            session.CancellationReason,
            ToContract(session.Progress),
            session.Version);

    private static StocktakeSessionListItemResponse ToContract(
        StocktakeSessionListItem session) =>
        new(
            session.Id,
            session.Name,
            ToContract(session.Scope),
            session.ScopeDisplayName,
            ToContract(session.Status),
            session.StartedAtUtc,
            session.StartedByDisplayName,
            session.ClosedAtUtc,
            ToContract(session.Progress),
            session.Version);

    private static StocktakeItemResponse ToContract(StocktakeItemView item) =>
        new(
            item.AssetId,
            item.AssetTag,
            item.DeviceName,
            new ReferenceSummaryResponse(
                item.CategoryId,
                item.CategoryCode,
                item.CategoryName),
            item.IsExpected,
            item.IsSeen,
            item.ExpectedStatus?.ToContract(),
            item.ExpectedCondition?.ToContract(),
            item.ExpectedLocationId.HasValue
                ? new LocationSummaryResponse(
                    item.ExpectedLocationId.Value,
                    item.ExpectedLocationCode ?? string.Empty,
                    item.ExpectedLocationName ?? string.Empty)
                : null,
            item.ObservedLocationId.HasValue
                ? new LocationSummaryResponse(
                    item.ObservedLocationId.Value,
                    item.ObservedLocationCode ?? string.Empty,
                    item.ObservedLocationName ?? string.Empty)
                : null,
            item.ObservedCondition?.ToContract(),
            item.Note,
            item.ObservedAtUtc,
            item.ObservedByUserId,
            item.ObservedByDisplayName,
            item.IsUnexpected,
            item.IsWrongLocation,
            item.IsConditionChanged);

    private static StocktakeReferenceResponse ToContract(
        StocktakeReferenceView reference) =>
        new(reference.Id, reference.Code, reference.Name, reference.IsActive);

    private static StocktakeProgressResponse ToContract(StocktakeProgress progress) =>
        new(
            progress.Expected,
            progress.Seen,
            progress.Missing,
            progress.Unexpected,
            progress.WrongLocation,
            progress.ConditionChanged);

    private static StocktakeScopeContract ToContract(StocktakeScope scope) =>
        scope switch
        {
            StocktakeScope.All => StocktakeScopeContract.All,
            StocktakeScope.Location => StocktakeScopeContract.Location,
            StocktakeScope.Category => StocktakeScopeContract.Category,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
        };

    private static StocktakeStatusContract ToContract(StocktakeStatus status) =>
        status switch
        {
            StocktakeStatus.InProgress => StocktakeStatusContract.InProgress,
            StocktakeStatus.Completed => StocktakeStatusContract.Completed,
            StocktakeStatus.Cancelled => StocktakeStatusContract.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
}
