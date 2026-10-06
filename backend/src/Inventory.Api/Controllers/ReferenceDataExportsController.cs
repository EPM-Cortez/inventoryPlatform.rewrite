using Inventory.Application.DataExchange;
using Inventory.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/exports")]
public sealed class ReferenceDataExportsController : ControllerBase
{
    private readonly ReferenceDataExchangeService _service;

    public ReferenceDataExportsController(ReferenceDataExchangeService service)
    {
        _service = service;
    }

    [HttpGet("people.csv", Name = "ExportPeopleCsvV1")]
    [Authorize(Policy = Permissions.ExportsPeople)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ExportPeople(CancellationToken cancellationToken) =>
        ExportAsync(_service.ExportPeopleAsync, cancellationToken);

    [HttpGet("departments.csv", Name = "ExportDepartmentsCsvV1")]
    [Authorize(Policy = Permissions.ExportsDepartments)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ExportDepartments(CancellationToken cancellationToken) =>
        ExportAsync(_service.ExportDepartmentsAsync, cancellationToken);

    [HttpGet("locations.csv", Name = "ExportLocationsCsvV1")]
    [Authorize(Policy = Permissions.ExportsLocations)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> ExportLocations(CancellationToken cancellationToken) =>
        ExportAsync(_service.ExportLocationsAsync, cancellationToken);

    private async Task<IActionResult> ExportAsync(
        Func<CancellationToken, Task<ReferenceDataCsvFile>> export,
        CancellationToken cancellationToken)
    {
        var file = await export(cancellationToken);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Append(
            "X-Inventory-Row-Count",
            file.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return File(file.Content, "text/csv; charset=utf-8", file.FileName);
    }
}
