using Inventory.Application.Abstractions;
using Inventory.Application.Security;
using Inventory.Contracts.V1;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/v1/agents")]
public sealed class AgentInstallerController : ControllerBase
{
    private const string DownloadPath =
        "/api/v1/agents/installer/download";
    private const string SafeInstallCommandTemplate =
        "msiexec.exe /i \"<downloaded-installer-path>\" /qn /norestart " +
        "API_BASE_URL=\"<inventory-api-base-url>\" " +
        "ENROLLMENT_TOKEN=\"<one-time-enrollment-token>\" " +
        "ALLOW_SOFTWARE_KEY=0";

    private readonly IAgentInstallerArtifactProvider _provider;

    public AgentInstallerController(
        IAgentInstallerArtifactProvider provider)
    {
        _provider = provider;
    }

    [HttpGet("installer", Name = "GetAgentInstallerMetadataV1")]
    [Authorize(Policy = Permissions.AgentsManage)]
    [Produces("application/json")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<AgentInstallerMetadataResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AgentInstallerMetadataResponse>>
        GetMetadata(CancellationToken cancellationToken)
    {
        SetNoStoreHeaders();
        var artifact = await _provider.GetMetadataAsync(cancellationToken);
        return Ok(
            new AgentInstallerMetadataResponse(
                artifact.FileName,
                artifact.Version,
                artifact.SizeBytes,
                artifact.Sha256,
                artifact.HasValidSignature,
                artifact.ReleaseAttestationVerified,
                artifact.Signer,
                DownloadPath,
                SafeInstallCommandTemplate));
    }

    [HttpGet("installer/download", Name = "DownloadAgentInstallerV1")]
    [Authorize(Policy = Permissions.AgentsManage)]
    [Produces("application/x-msi")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Download(
        CancellationToken cancellationToken)
    {
        SetNoStoreHeaders();
        var download = await _provider.OpenDownloadAsync(cancellationToken);
        Response.ContentLength = download.Metadata.SizeBytes;
        return File(
            download.Content,
            "application/x-msi",
            download.Metadata.FileName,
            enableRangeProcessing: false);
    }

    private void SetNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store, max-age=0";
        Response.Headers.Pragma = "no-cache";
    }
}
