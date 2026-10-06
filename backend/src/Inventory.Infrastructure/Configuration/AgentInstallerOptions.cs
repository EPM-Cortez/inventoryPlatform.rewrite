namespace Inventory.Infrastructure.Configuration;

public sealed class AgentInstallerOptions
{
    public string InstallerPath { get; init; } = string.Empty;

    public string ManifestPath { get; init; } = string.Empty;

    public string ManifestSignaturePath { get; init; } = string.Empty;

    public string TrustedSigningCertificatePath { get; init; } = string.Empty;

    public string ApprovedVersion { get; init; } = string.Empty;

    public string ApprovedSha256 { get; init; } = string.Empty;

    public long MaximumArtifactBytes { get; init; } = 1_073_741_824;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(InstallerPath) &&
        !string.IsNullOrWhiteSpace(ManifestPath);

    public bool HasReleaseAttestation =>
        !string.IsNullOrWhiteSpace(ManifestSignaturePath) &&
        !string.IsNullOrWhiteSpace(TrustedSigningCertificatePath) &&
        !string.IsNullOrWhiteSpace(ApprovedVersion) &&
        !string.IsNullOrWhiteSpace(ApprovedSha256);

    public void Validate()
    {
        var hasInstaller = !string.IsNullOrWhiteSpace(InstallerPath);
        var hasManifest = !string.IsNullOrWhiteSpace(ManifestPath);
        if (hasInstaller != hasManifest)
        {
            throw new InvalidOperationException(
                "AgentInstaller:InstallerPath and ManifestPath must be configured together.");
        }

        var anyReleaseAttestationField =
            !string.IsNullOrWhiteSpace(ManifestSignaturePath) ||
            !string.IsNullOrWhiteSpace(TrustedSigningCertificatePath) ||
            !string.IsNullOrWhiteSpace(ApprovedVersion) ||
            !string.IsNullOrWhiteSpace(ApprovedSha256);
        if (anyReleaseAttestationField && !HasReleaseAttestation)
        {
            throw new InvalidOperationException(
                "All AgentInstaller release-attestation and approved-artifact settings must be configured together.");
        }

        if (anyReleaseAttestationField && !hasInstaller)
        {
            throw new InvalidOperationException(
                "AgentInstaller release attestation requires an installer and manifest.");
        }

        if (MaximumArtifactBytes is < 1_048_576 or > 2_147_483_648)
        {
            throw new InvalidOperationException(
                "AgentInstaller:MaximumArtifactBytes must be between 1 MiB and 2 GiB.");
        }

        if (!hasInstaller)
        {
            return;
        }

        if (!Path.IsPathFullyQualified(InstallerPath) ||
            !Path.IsPathFullyQualified(ManifestPath))
        {
            throw new InvalidOperationException(
                "Agent installer and manifest paths must be absolute.");
        }

        if (!string.Equals(
                Path.GetExtension(InstallerPath),
                ".msi",
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetExtension(ManifestPath),
                ".json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Agent installer paths must identify an MSI and a JSON manifest.");
        }

        if (HasReleaseAttestation &&
            (!Path.IsPathFullyQualified(ManifestSignaturePath) ||
             !Path.IsPathFullyQualified(TrustedSigningCertificatePath) ||
             !string.Equals(Path.GetExtension(ManifestSignaturePath), ".sig", StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(Path.GetExtension(TrustedSigningCertificatePath), ".pem", StringComparison.OrdinalIgnoreCase) ||
             ApprovedSha256.Length != 64 ||
             ApprovedSha256.Any(character => !char.IsAsciiHexDigit(character)) ||
             ApprovedVersion.Length > 100))
        {
            throw new InvalidOperationException(
                "AgentInstaller release attestation requires absolute .sig/.pem paths, an approved version, and a SHA-256 digest.");
        }
    }
}
