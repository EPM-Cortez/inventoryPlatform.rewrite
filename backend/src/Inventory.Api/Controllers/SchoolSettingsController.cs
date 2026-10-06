using Inventory.Application.School;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/settings/school")]
[Produces("application/json")]
[Authorize(Policy = Permissions.SettingsManage)]
public sealed class SchoolSettingsController : ControllerBase
{
    private readonly SchoolSettingsService _service;

    public SchoolSettingsController(SchoolSettingsService service)
    {
        _service = service;
    }

    [HttpGet(Name = "GetSchoolSettingsV1")]
    [ProducesResponseType<SchoolSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SchoolSettingsResponse>> Get(
        CancellationToken cancellationToken)
    {
        var settings = await _service.GetAsync(cancellationToken);
        return Ok(ToContract(settings));
    }

    [HttpPut(Name = "UpdateSchoolSettingsV1")]
    [ProducesResponseType<SchoolSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SchoolSettingsResponse>> Update(
        [FromBody] UpdateSchoolSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await _service.UpdateAsync(
            new UpdateSchoolSettingsCommand(
                request.Name,
                request.Code,
                request.TimeZone,
                request.QrBaseUrl,
                request.WarrantyWarningDays,
                request.Version),
            cancellationToken);
        return Ok(ToContract(settings));
    }

    private static SchoolSettingsResponse ToContract(SchoolSettingsView settings) =>
        new(
            settings.Name,
            settings.Code,
            settings.TimeZone,
            settings.QrBaseUrl,
            settings.WarrantyWarningDays,
            settings.IsPersisted,
            settings.Version);
}
