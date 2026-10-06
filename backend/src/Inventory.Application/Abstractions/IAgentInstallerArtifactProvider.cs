using Inventory.Application.Agents;

namespace Inventory.Application.Abstractions;

public interface IAgentInstallerArtifactProvider
{
    Task<AgentInstallerArtifactMetadata> GetMetadataAsync(
        CancellationToken cancellationToken);

    Task<AgentInstallerArtifactDownload> OpenDownloadAsync(
        CancellationToken cancellationToken);
}
