using System.Text.Json.Serialization;

namespace Inventory.Contracts.V1;

public sealed record AgentInstallerMetadataResponse(
    string FileName,
    string Version,
    long SizeBytes,
    string Sha256,
    [property: JsonPropertyName("signed")] bool HasValidSignature,
    bool ReleaseAttestationVerified,
    string? Signer,
    string DownloadUrl,
    string InstallCommandTemplate);
