using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Agents;
using Inventory.Application.Common;
using Inventory.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Agents;

public sealed class AgentInstallerArtifactProvider
    : IAgentInstallerArtifactProvider
{
    private const int MaximumManifestBytes = 1_048_576;
    private static readonly JsonSerializerOptions ManifestJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            MaxDepth = 8,
            PropertyNameCaseInsensitive = true,
        };

    private readonly AgentInstallerOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly bool _isWindows;

    public AgentInstallerArtifactProvider(
        IOptions<AgentInstallerOptions> options,
        IHostEnvironment environment)
        : this(options, environment, OperatingSystem.IsWindows())
    {
    }

    internal AgentInstallerArtifactProvider(
        IOptions<AgentInstallerOptions> options,
        IHostEnvironment environment,
        bool isWindows)
    {
        _options = options.Value;
        _environment = environment;
        _isWindows = isWindows;
    }

    public async Task<AgentInstallerArtifactMetadata> GetMetadataAsync(
        CancellationToken cancellationToken)
    {
        var download = await OpenValidatedArtifactAsync(cancellationToken);
        await download.Content.DisposeAsync();
        return download.Metadata;
    }

    public Task<AgentInstallerArtifactDownload> OpenDownloadAsync(
        CancellationToken cancellationToken) =>
        OpenValidatedArtifactAsync(cancellationToken);

    private async Task<AgentInstallerArtifactDownload>
        OpenValidatedArtifactAsync(CancellationToken cancellationToken)
    {
        try
        {
            _options.Validate();
            if (!_options.IsConfigured)
            {
                throw ArtifactNotFound();
            }

            var installerPath = Path.GetFullPath(_options.InstallerPath);
            var manifestPath = Path.GetFullPath(_options.ManifestPath);
            EnsureSafeConfiguredFile(installerPath, ".msi");
            EnsureSafeConfiguredFile(manifestPath, ".json");

            var (manifest, manifestBytes) = await ReadManifestAsync(
                manifestPath,
                cancellationToken);
            var releaseSigner = await VerifyReleaseAttestationAsync(
                manifestPath,
                manifestBytes,
                cancellationToken);
            var installerFileName = Path.GetFileName(installerPath);
            EnsureSafeDownloadFileName(installerFileName);
            var manifestFiles = manifest.Files
                .Where(
                    file =>
                        string.Equals(
                            file.File,
                            installerFileName,
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (manifestFiles.Length != 1)
            {
                throw ArtifactInvalid();
            }

            var manifestFile = manifestFiles[0];
            ValidateManifestFile(manifestFile, installerFileName);
            if (string.IsNullOrWhiteSpace(manifest.Version) ||
                manifest.Version.Length > 100)
            {
                throw ArtifactInvalid();
            }

            if (releaseSigner is not null &&
                (!manifestFile.SignaturePresent ||
                 !string.Equals(manifest.Version, _options.ApprovedVersion, StringComparison.Ordinal) ||
                 !string.Equals(manifestFile.Sha256, _options.ApprovedSha256, StringComparison.OrdinalIgnoreCase)))
            {
                throw ArtifactInvalid();
            }

            var stream = new FileStream(
                installerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 65_536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            try
            {
                if (stream.Length < 1 ||
                    stream.Length > _options.MaximumArtifactBytes ||
                    stream.Length != manifestFile.Bytes)
                {
                    throw ArtifactInvalid();
                }

                var actualHash = await SHA256.HashDataAsync(
                    stream,
                    cancellationToken);
                var expectedHash = Convert.FromHexString(
                    manifestFile.Sha256);
                if (!CryptographicOperations.FixedTimeEquals(
                        actualHash,
                        expectedHash))
                {
                    throw ArtifactInvalid();
                }

                var signature = VerifySignature(
                    installerPath,
                    manifestFile.SignaturePresent,
                    releaseSigner);
                stream.Position = 0;
                return new AgentInstallerArtifactDownload(
                    new AgentInstallerArtifactMetadata(
                        installerFileName,
                        manifest.Version.Trim(),
                        stream.Length,
                        Convert.ToHexString(actualHash).ToLowerInvariant(),
                        signature.HasValidSignature,
                        signature.ReleaseAttestationVerified,
                        signature.Signer),
                    stream);
            }
            catch
            {
                await stream.DisposeAsync();
                throw;
            }
        }
        catch (InventoryException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                CryptographicException or
                FormatException or
                ArgumentException or
                NotSupportedException or
                InvalidOperationException)
        {
            throw ArtifactInvalid(exception);
        }
    }

    private static async Task<(AgentArtifactManifest Manifest, byte[] Bytes)> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16_384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is < 1 or > MaximumManifestBytes)
        {
            throw ArtifactInvalid();
        }

        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        var manifest = JsonSerializer.Deserialize<AgentArtifactManifest>(
            bytes,
            ManifestJsonOptions) ?? throw ArtifactInvalid();
        return (manifest, bytes);
    }

    private async Task<string?> VerifyReleaseAttestationAsync(
        string manifestPath,
        byte[] manifestBytes,
        CancellationToken cancellationToken)
    {
        if (!_options.HasReleaseAttestation)
        {
            if (_environment.IsProduction() && !_isWindows)
            {
                throw ArtifactInvalid();
            }

            return null;
        }

        var signaturePath = Path.GetFullPath(_options.ManifestSignaturePath);
        var trustedCertificatePath = Path.GetFullPath(
            _options.TrustedSigningCertificatePath);
        EnsureSafeConfiguredFile(signaturePath, ".sig");
        EnsureSafeConfiguredFile(trustedCertificatePath, ".pem");

        var artifactRoot = Path.GetDirectoryName(manifestPath)
            ?? throw ArtifactInvalid();
        var certificateRelativePath = Path.GetRelativePath(
            artifactRoot,
            trustedCertificatePath);
        if (!certificateRelativePath.StartsWith("..", StringComparison.Ordinal) &&
            !Path.IsPathRooted(certificateRelativePath))
        {
            // The signing trust anchor must not be supplied by the artifact itself.
            throw ArtifactInvalid();
        }

        var signatureFile = new FileInfo(signaturePath);
        if (signatureFile.Length is < 64 or > 1_024)
        {
            throw ArtifactInvalid();
        }

        var signatureBytes = await File.ReadAllBytesAsync(
            signaturePath,
            cancellationToken);
        if (signatureBytes.Length != signatureFile.Length)
        {
            throw ArtifactInvalid();
        }

        using var certificate = X509CertificateLoader.LoadCertificateFromFile(
            trustedCertificatePath);
        using var publicKey = certificate.GetRSAPublicKey();
        if (publicKey is null ||
            !publicKey.VerifyData(
                manifestBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1))
        {
            throw ArtifactInvalid();
        }

        return certificate.Subject;
    }

    private SignatureDetails VerifySignature(
        string installerPath,
        bool manifestSigned,
        string? releaseSigner)
    {
        if (!manifestSigned)
        {
            if (_environment.IsProduction())
            {
                throw ArtifactInvalid();
            }

            return new SignatureDetails(false, false, null);
        }

        if (!_isWindows)
        {
            if (releaseSigner is null)
            {
                throw ArtifactInvalid();
            }

            return new SignatureDetails(
                false,
                true,
                $"Release attestation: {releaseSigner}");
        }

        if (!WindowsAuthenticodeVerifier.IsTrusted(installerPath))
        {
            throw ArtifactInvalid();
        }

        string signer;
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate =
                X509Certificate.CreateFromSignedFile(installerPath);
#pragma warning restore SYSLIB0057
            signer = certificate.Subject;
        }
        catch (CryptographicException exception)
        {
            throw ArtifactInvalid(exception);
        }

        if (string.IsNullOrWhiteSpace(signer))
        {
            throw ArtifactInvalid();
        }

        return new SignatureDetails(true, releaseSigner is not null, signer);
    }

    private static void EnsureSafeConfiguredFile(
        string path,
        string expectedExtension)
    {
        if (!Path.IsPathFullyQualified(path) ||
            !string.Equals(
                Path.GetExtension(path),
                expectedExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw ArtifactInvalid();
        }

        if (!File.Exists(path))
        {
            throw ArtifactNotFound();
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw ArtifactInvalid();
        }
    }

    private static void EnsureSafeDownloadFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
            fileName.Any(character => char.IsControl(character) || character == '"'))
        {
            throw ArtifactInvalid();
        }
    }

    private static void ValidateManifestFile(
        AgentArtifactManifestFile file,
        string expectedFileName)
    {
        if (!string.Equals(
                file.File,
                expectedFileName,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                file.File,
                Path.GetFileName(file.File),
                StringComparison.Ordinal) ||
            file.Bytes < 1 ||
            file.Sha256.Length != 64 ||
            file.Sha256.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw ArtifactInvalid();
        }

        if (!string.IsNullOrWhiteSpace(file.Path) &&
            !string.Equals(
                file.Path
                    .Replace('\\', '/')
                    .Split(
                        '/',
                        StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault(),
                expectedFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw ArtifactInvalid();
        }
    }

    private static ResourceNotFoundException ArtifactNotFound() =>
        new("Agent installer artifact", "configured");

    private static ResourceConflictException ArtifactInvalid(
        Exception? innerException = null) =>
        new(
            "agent_installer_artifact_invalid",
            "The configured agent installer artifact failed integrity validation.",
            innerException);

    private sealed class AgentArtifactManifest
    {
        public string Version { get; init; } = string.Empty;

        public IReadOnlyList<AgentArtifactManifestFile> Files { get; init; } = [];
    }

    private sealed class AgentArtifactManifestFile
    {
        public string File { get; init; } = string.Empty;

        public string? Path { get; init; }

        public long Bytes { get; init; }

        public string Sha256 { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("signed")]
        public bool SignaturePresent { get; init; }
    }

    private sealed record SignatureDetails(
        bool HasValidSignature,
        bool ReleaseAttestationVerified,
        string? Signer);

    private static class WindowsAuthenticodeVerifier
    {
        private static readonly Guid GenericVerifyV2 =
            new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

        public static bool IsTrusted(string path)
        {
            var filePathPointer = Marshal.StringToCoTaskMemUni(path);
            var fileInfoPointer = IntPtr.Zero;
            try
            {
                var fileInfo = new WinTrustFileInfo
                {
                    StructSize =
                        (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                    FilePath = filePathPointer,
                };
                fileInfoPointer = Marshal.AllocCoTaskMem(
                    Marshal.SizeOf<WinTrustFileInfo>());
                Marshal.StructureToPtr(
                    fileInfo,
                    fileInfoPointer,
                    false);
                var trustData = new WinTrustData
                {
                    StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                    UiChoice = 2,
                    RevocationChecks = 0,
                    UnionChoice = 1,
                    FileInfo = fileInfoPointer,
                    StateAction = 0,
                    ProviderFlags = 0x1000,
                    UiContext = 0,
                };
                var action = GenericVerifyV2;
                return WinVerifyTrust(
                        IntPtr.Zero,
                        ref action,
                        ref trustData) == 0;
            }
            finally
            {
                if (fileInfoPointer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(fileInfoPointer);
                }

                Marshal.FreeCoTaskMem(filePathPointer);
            }
        }

        [DllImport(
            "wintrust.dll",
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int WinVerifyTrust(
            IntPtr windowHandle,
            ref Guid actionId,
            ref WinTrustData trustData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructSize;
            public IntPtr FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint StructSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;
        }
    }
}
