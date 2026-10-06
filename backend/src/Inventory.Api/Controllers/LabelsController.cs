using System.Globalization;
using Inventory.Api.Mapping;
using Inventory.Application.Labels;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/labels")]
public sealed class LabelsController : ControllerBase
{
    private readonly LabelService _labelService;

    public LabelsController(LabelService labelService)
    {
        _labelService = labelService;
    }

    [HttpGet("stocks", Name = "ListLabelStocksV1")]
    [Authorize(Policy = Permissions.QrPrint)]
    [ProducesResponseType<LabelStockResponse[]>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<LabelStockResponse>> ListStocks() =>
        Ok(LabelService.ListStocks().Select(stock => stock.ToContract()));

    [HttpPost("batch", Name = "PrintAssetLabelBatchV1")]
    [Authorize(Policy = Permissions.QrPrint)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PrintBatch(
        PrintLabelBatchRequest request,
        CancellationToken cancellationToken)
    {
        var document = await _labelService.PrintAsync(
            new PrintLabelsCommand(
                request.Stock.ToDomain(),
                request.StartPosition,
                request.Items
                    .Select(item => new LabelBatchItem(item.AssetId, item.Quantity))
                    .ToArray(),
                request.IssueMissingQr),
            "asset-labels.pdf",
            cancellationToken);
        return Pdf(document);
    }

    private FileContentResult Pdf(LabelPdfDocument document)
    {
        Response.Headers.CacheControl = "no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.ContentDisposition =
            $"inline; filename=\"{document.FileName}\"";
        Response.Headers["X-Label-Count"] =
            document.LabelCount.ToString(CultureInfo.InvariantCulture);
        Response.Headers["X-Label-Page-Count"] =
            document.PageCount.ToString(CultureInfo.InvariantCulture);
        return File(document.Content, "application/pdf");
    }
}
