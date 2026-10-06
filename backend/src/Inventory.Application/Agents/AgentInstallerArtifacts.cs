namespace Inventory.Application.Agents;

public sealed record AgentInstallerArtifactMetadata(
    string FileName,
    string Version,
    long SizeBytes,
    string Sha256,
    bool HasValidSignature,
    bool ReleaseAttestationVerified,
    string? Signer);

public sealed record AgentInstallerArtifactDownload(
    AgentInstallerArtifactMetadata Metadata,
    Stream Content);
