using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Inventory.Application.Agents;
using Inventory.Application.Common;
using Inventory.Contracts.V1;
using Inventory.Infrastructure.Agents;
using Inventory.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Inventory.Api.IntegrationTests;

public sealed class AgentInstallerApiTests
    : IClassFixture<InventoryApiFactory>
{
    private readonly InventoryApiFactory _factory;

    public AgentInstallerApiTests(InventoryApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InstallerEndpointsRequireAgentsManage()
    {
        using var anonymous = _factory.CreateHttpsClient();
        using var anonymousResponse = await anonymous.GetAsync(
            "/api/v1/agents/installer");
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            anonymousResponse.StatusCode);

        using var technician =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.TechnicianEmail,
                InventoryApiFactory.TechnicianPassword);
        using var technicianResponse = await technician.GetAsync(
            "/api/v1/agents/installer");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            technicianResponse.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanInspectAndStreamVerifiedArtifact()
    {
        await using var artifact = await TestArtifact.CreateAsync();
        using var initializationClient =
            await _factory.CreateAuthenticatedClientAsync(
                InventoryApiFactory.AdministratorEmail,
                InventoryApiFactory.AdministratorPassword);
        using var configuredFactory = _factory.WithWebHostBuilder(
            builder =>
            {
                builder.UseSetting(
                    "AgentInstaller:InstallerPath",
                    artifact.InstallerPath);
                builder.UseSetting(
                    "AgentInstaller:ManifestPath",
                    artifact.ManifestPath);
            });
        using var administrator =
            await CreateAuthenticatedClientAsync(configuredFactory);

        using var metadataResponse = await administrator.GetAsync(
            "/api/v1/agents/installer");
        metadataResponse.EnsureSuccessStatusCode();
        Assert.True(metadataResponse.Headers.CacheControl?.NoStore);
        var metadata = await metadataResponse.Content
            .ReadFromJsonAsync<AgentInstallerMetadataResponse>(
                ApiTestClient.JsonOptions);
        Assert.NotNull(metadata);
        Assert.Equal(artifact.FileName, metadata.FileName);
        Assert.Equal(TestArtifact.Version, metadata.Version);
        Assert.Equal(artifact.Bytes.LongLength, metadata.SizeBytes);
        Assert.Equal(artifact.Sha256, metadata.Sha256);
        Assert.False(metadata.HasValidSignature);
        Assert.False(metadata.ReleaseAttestationVerified);
        Assert.Null(metadata.Signer);
        Assert.Equal(
            "/api/v1/agents/installer/download",
            metadata.DownloadUrl);
        Assert.Contains(
            "ENROLLMENT_TOKEN=\"<one-time-enrollment-token>\"",
            metadata.InstallCommandTemplate,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "inv_enroll",
            metadata.InstallCommandTemplate,
            StringComparison.OrdinalIgnoreCase);

        using var downloadResponse = await administrator.GetAsync(
            metadata.DownloadUrl);
        downloadResponse.EnsureSuccessStatusCode();
        Assert.True(downloadResponse.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "application/x-msi",
            downloadResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            artifact.Bytes.LongLength,
            downloadResponse.Content.Headers.ContentLength);
        Assert.Equal(
            artifact.Bytes,
            await downloadResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ProviderRejectsHashMismatchAndKeepsArtifactClosed()
    {
        await using var artifact = await TestArtifact.CreateAsync(
            validHash: false);
        var provider = CreateProvider(
            artifact,
            Environments.Development);

        var exception = await Assert.ThrowsAsync<ResourceConflictException>(
            () => provider.OpenDownloadAsync(CancellationToken.None));

        Assert.Equal("agent_installer_artifact_invalid", exception.Code);
    }

    [Fact]
    public async Task ProviderStreamsValidatedFileInsteadOfBufferingIt()
    {
        await using var artifact = await TestArtifact.CreateAsync();
        var provider = CreateProvider(
            artifact,
            Environments.Development);

        var download = await provider.OpenDownloadAsync(
            CancellationToken.None);
        await using (download.Content)
        {
            Assert.IsType<FileStream>(download.Content);
            Assert.Equal(0, download.Content.Position);
            Assert.Equal(artifact.Bytes.LongLength, download.Content.Length);
        }
    }

    [Fact]
    public async Task ProductionRejectsUnsignedArtifact()
    {
        await using var artifact = await TestArtifact.CreateAsync();
        var provider = CreateProvider(
            artifact,
            Environments.Production);

        var exception = await Assert.ThrowsAsync<ResourceConflictException>(
            () => provider.GetMetadataAsync(CancellationToken.None));

        Assert.Equal("agent_installer_artifact_invalid", exception.Code);
    }

    [Fact]
    public async Task LinuxProductionAcceptsOnlyPinnedSignedReleaseArtifact()
    {
        await using var artifact = await TestArtifact.CreateAsync(hasSignature: true);
        var provider = CreateLinuxProductionProvider(artifact);

        var download = await provider.OpenDownloadAsync(CancellationToken.None);
        await using (download.Content)
        {
            Assert.Equal(TestArtifact.Version, download.Metadata.Version);
            Assert.Equal(artifact.FileName, download.Metadata.FileName);
            Assert.Equal(artifact.Bytes.LongLength, download.Metadata.SizeBytes);
            Assert.Equal(artifact.Sha256, download.Metadata.Sha256);
            Assert.False(download.Metadata.HasValidSignature);
            Assert.True(download.Metadata.ReleaseAttestationVerified);
            Assert.StartsWith(
                "Release attestation:",
                download.Metadata.Signer,
                StringComparison.Ordinal);
            Assert.Equal(artifact.Bytes, await ReadAllBytesAsync(download.Content));
        }
    }

    [Fact]
    public async Task LinuxProductionRejectsTamperedManifestAndWrongApprovedDigest()
    {
        await using var artifact = await TestArtifact.CreateAsync(hasSignature: true);
        var wrongPin = CreateLinuxProductionProvider(
            artifact,
            approvedSha256: new string('0', 64));
        await Assert.ThrowsAsync<ResourceConflictException>(
            () => wrongPin.GetMetadataAsync(CancellationToken.None));

        await File.AppendAllTextAsync(artifact.ManifestPath, " ");
        var tampered = CreateLinuxProductionProvider(artifact);
        await Assert.ThrowsAsync<ResourceConflictException>(
            () => tampered.GetMetadataAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LinuxProductionRejectsUnsignedArtifactWithoutAttestation()
    {
        await using var artifact = await TestArtifact.CreateAsync();
        var provider = new AgentInstallerArtifactProvider(
            Options.Create(
                new AgentInstallerOptions
                {
                    InstallerPath = artifact.InstallerPath,
                    ManifestPath = artifact.ManifestPath,
                }),
            new TestHostEnvironment(Environments.Production),
            isWindows: false);

        await Assert.ThrowsAsync<ResourceConflictException>(
            () => provider.GetMetadataAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ProviderFailsClosedForMissingOrRelativePaths()
    {
        await using var artifact = await TestArtifact.CreateAsync();
        var missing = new AgentInstallerArtifactProvider(
            Options.Create(
                new AgentInstallerOptions
                {
                    InstallerPath = Path.Combine(
                        artifact.DirectoryPath,
                        "missing.msi"),
                    ManifestPath = artifact.ManifestPath,
                }),
            new TestHostEnvironment(Environments.Development));
        await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => missing.GetMetadataAsync(CancellationToken.None));

        var relative = new AgentInstallerArtifactProvider(
            Options.Create(
                new AgentInstallerOptions
                {
                    InstallerPath = "relative.msi",
                    ManifestPath = "relative.json",
                }),
            new TestHostEnvironment(Environments.Development));
        var exception = await Assert.ThrowsAsync<ResourceConflictException>(
            () => relative.GetMetadataAsync(CancellationToken.None));
        Assert.Equal("agent_installer_artifact_invalid", exception.Code);
    }

    private static AgentInstallerArtifactProvider CreateProvider(
        TestArtifact artifact,
        string environmentName) =>
        new(
            Options.Create(
                new AgentInstallerOptions
                {
                    InstallerPath = artifact.InstallerPath,
                    ManifestPath = artifact.ManifestPath,
                }),
            new TestHostEnvironment(environmentName));

    private static AgentInstallerArtifactProvider CreateLinuxProductionProvider(
        TestArtifact artifact,
        string? approvedSha256 = null) =>
        new(
            Options.Create(
                new AgentInstallerOptions
                {
                    InstallerPath = artifact.InstallerPath,
                    ManifestPath = artifact.ManifestPath,
                    ManifestSignaturePath = artifact.SignaturePath,
                    TrustedSigningCertificatePath = artifact.CertificatePath,
                    ApprovedVersion = TestArtifact.Version,
                    ApprovedSha256 = approvedSha256 ?? artifact.Sha256,
                }),
            new TestHostEnvironment(Environments.Production),
            isWindows: false);

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var output = new MemoryStream();
        await stream.CopyToAsync(output);
        return output.ToArray();
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
            });
        var csrf = await ApiTestClient.GetCsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email = InventoryApiFactory.AdministratorEmail,
                password = InventoryApiFactory.AdministratorPassword,
                rememberMe = false,
            });
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        return client;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "Inventory.Tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class TestArtifact : IAsyncDisposable
    {
        public const string Version = "1.2.3";

        private TestArtifact(
            string directoryPath,
            string trustDirectoryPath,
            string installerPath,
            string manifestPath,
            byte[] bytes,
            string sha256)
        {
            DirectoryPath = directoryPath;
            TrustDirectoryPath = trustDirectoryPath;
            InstallerPath = installerPath;
            ManifestPath = manifestPath;
            Bytes = bytes;
            Sha256 = sha256;
        }

        public string DirectoryPath { get; }

        public string TrustDirectoryPath { get; }

        public string InstallerPath { get; }

        public string ManifestPath { get; }

        public string SignaturePath => ManifestPath + ".sig";

        public string CertificatePath => Path.Combine(
            TrustDirectoryPath,
            "agent-release-certificate.pem");

        public string FileName => Path.GetFileName(InstallerPath);

        public byte[] Bytes { get; }

        public string Sha256 { get; }

        public static async Task<TestArtifact> CreateAsync(
            bool validHash = true,
            bool hasSignature = false)
        {
            var directoryPath = Path.Combine(
                Path.GetTempPath(),
                $"inventory-agent-installer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directoryPath);
            var trustDirectoryPath = directoryPath + "-trust";
            Directory.CreateDirectory(trustDirectoryPath);
            var installerPath = Path.Combine(
                directoryPath,
                "Inventory.Agent.Installer.msi");
            var manifestPath = Path.Combine(
                directoryPath,
                "agent-artifacts.json");
            var bytes = RandomNumberGenerator.GetBytes(128 * 1024);
            await File.WriteAllBytesAsync(installerPath, bytes);
            var sha256 = Convert.ToHexString(
                    SHA256.HashData(bytes))
                .ToLowerInvariant();
            var manifestHash = validHash
                ? sha256
                : new string('0', 64);
            var manifest = JsonSerializer.Serialize(
                new
                {
                    version = Version,
                    files = new[]
                    {
                        new
                        {
                            file = Path.GetFileName(installerPath),
                            path = installerPath,
                            bytes = bytes.LongLength,
                            sha256 = manifestHash,
                            signed = hasSignature,
                        },
                    },
                },
                ApiTestClient.JsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifest);
            if (hasSignature)
            {
                using var rsa = RSA.Create(2048);
                var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                    "CN=Inventory Test Release",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                using var certificate = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddDays(1));
                await File.WriteAllBytesAsync(
                    manifestPath + ".sig",
                    rsa.SignData(
                        await File.ReadAllBytesAsync(manifestPath),
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1));
                await File.WriteAllTextAsync(
                    Path.Combine(trustDirectoryPath, "agent-release-certificate.pem"),
                    certificate.ExportCertificatePem());
            }
            return new TestArtifact(
                directoryPath,
                trustDirectoryPath,
                installerPath,
                manifestPath,
                bytes,
                sha256);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }

            if (Directory.Exists(TrustDirectoryPath))
            {
                Directory.Delete(TrustDirectoryPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
