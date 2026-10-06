using Inventory.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.IntegrationTests;

public sealed class ProductionStartupTests : IDisposable
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private readonly string _rootDirectory = Path.Combine(
        RepositoryRoot,
        "artifacts",
        "production-startup-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void FullyConfiguredProductionHostStarts()
    {
        using var factory = CreateProductionFactory(migrateDatabase: true);

        _ = factory.Services;
    }

    [Theory]
    [InlineData(
        "Security:DataProtectionKeysPath",
        "",
        "Security:DataProtectionKeysPath is required")]
    [InlineData(
        "Storage:BackupPath",
        "",
        "Storage:BackupPath must be an absolute local durable-storage path")]
    [InlineData(
        "Cors:AllowedOrigins:0",
        "https://*.example.school",
        "cannot contain wildcards")]
    [InlineData(
        "School:QrBaseUrl",
        "https://localhost/scan",
        "non-loopback absolute HTTPS URL")]
    [InlineData(
        "School:QrBaseUrl",
        "http://inventory.example.school/scan",
        "non-loopback absolute HTTPS URL")]
    [InlineData(
        "AllowedHosts",
        "",
        "AllowedHosts must list the real service host name")]
    [InlineData(
        "AllowedHosts",
        "*",
        "AllowedHosts must list the real service host name")]
    [InlineData(
        "AllowedHosts",
        "localhost;127.0.0.1",
        "AllowedHosts must list the real service host name")]
    [InlineData(
        "Identity:ReconcileRolePresetsOnStartup",
        "false",
        "must remain enabled in Production")]
    [InlineData(
        "Mfa:EnforceRequiredRoles",
        "false",
        "must remain enabled in Production")]
    [InlineData(
        "Mfa:RequiredRoles:0",
        "Technician",
        "must include Administrator in Production")]
    [InlineData(
        "Storage:MinimumDatabaseFreeSpaceBytes",
        "1",
        "must be between 64 MB and 10 TB")]
    [InlineData(
        "AttachmentScanning:Enabled",
        "false",
        "must remain true in Production")]
    public void InvalidProductionConfigurationFailsStartup(
        string key,
        string value,
        string expectedMessageFragment)
    {
        var requiresMigratedDatabase =
            key is "Mfa:RequiredRoles:0" or
                "Storage:MinimumDatabaseFreeSpaceBytes";
        using var factory = CreateProductionFactory(
            new Dictionary<string, string?> { [key] = value },
            migrateDatabase: requiresMigratedDatabase);

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains(
            expectedMessageFragment,
            Flatten(exception),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MissingConfiguredAttachmentScannerFailsStartup()
    {
        var executablePath = Path.Combine(
            _rootDirectory,
            "missing-scanner",
            "MpCmdRun.exe");
        using var factory = CreateProductionFactory(
            new Dictionary<string, string?>
            {
                ["AttachmentScanning:ExecutablePath"] = executablePath,
            },
            migrateDatabase: true);

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains(
            "must identify an existing scanner executable",
            Flatten(exception),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "Data Source=:memory:",
        "persistent SQLite database file")]
    [InlineData(
        "Data Source=data/inventory.db;Foreign Keys=True",
        "absolute SQLite Data Source path")]
    public void InvalidProductionDatabasePathFailsStartup(
        string connectionString,
        string expectedMessageFragment)
    {
        using var factory = CreateProductionFactory(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:InventoryDatabase"] = connectionString,
            });

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains(
            expectedMessageFragment,
            Flatten(exception),
            StringComparison.Ordinal);
    }

    [Fact]
    public void NetworkAndDeploymentDirectoryStorageFailStartup()
    {
        var deploymentDatabase = Path.Combine(
            RepositoryRoot,
            "src",
            "Inventory.Api",
            "data",
            "inventory.db");
        using var deploymentFactory = CreateProductionFactory(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:InventoryDatabase"] =
                    $"Data Source={deploymentDatabase};Foreign Keys=True",
            });
        var deploymentException = Record.Exception(
            () => _ = deploymentFactory.Services);
        Assert.NotNull(deploymentException);
        Assert.Contains(
            "deployed application directory",
            Flatten(deploymentException),
            StringComparison.Ordinal);

        using var networkFactory = CreateProductionFactory(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:InventoryDatabase"] =
                    @"Data Source=\\server\inventory\inventory.db;Foreign Keys=True",
            });
        var networkException = Record.Exception(() => _ = networkFactory.Services);
        Assert.NotNull(networkException);
        Assert.Contains(
            "network storage",
            Flatten(networkException),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ForwardedHeadersRequireAnExplicitEnabledTrustedTopology()
    {
        using var missingTrust = CreateProductionFactory(
            new Dictionary<string, string?>
            {
                ["ForwardedHeaders:Enabled"] = "true",
            });
        var missingTrustException = Record.Exception(() => _ = missingTrust.Services);
        Assert.NotNull(missingTrustException);
        Assert.Contains(
            "requires at least one",
            Flatten(missingTrustException),
            StringComparison.Ordinal);

        using var ignoredTrust = CreateProductionFactory(
            new Dictionary<string, string?>
            {
                ["ForwardedHeaders:Enabled"] = "false",
                ["ForwardedHeaders:KnownProxies:0"] = "127.0.0.1",
            });
        var ignoredTrustException = Record.Exception(() => _ = ignoredTrust.Services);
        Assert.NotNull(ignoredTrustException);
        Assert.Contains(
            "Enabled must be true",
            Flatten(ignoredTrustException),
            StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateProductionFactory(
        IReadOnlyDictionary<string, string?>? overrides = null,
        bool migrateDatabase = false)
    {
        var keyDirectory = Path.Combine(_rootDirectory, "keys");
        var backupDirectory = Path.Combine(_rootDirectory, "backups");
        var databasePath = Path.Combine(_rootDirectory, "data", "inventory.db");
        Directory.CreateDirectory(keyDirectory);

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:InventoryDatabase"] =
                $"Data Source={databasePath};Foreign Keys=True",
            ["Security:DataProtectionKeysPath"] = keyDirectory,
            ["Storage:BackupPath"] = backupDirectory,
            ["School:QrBaseUrl"] = "https://inventory.example.school/scan",
            ["AllowedHosts"] = "inventory.example.school",
        };
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        if (migrateDatabase)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlite(settings["ConnectionStrings:InventoryDatabase"])
                .Options;
            using var dbContext = new InventoryDbContext(options);
            dbContext.Database.Migrate();
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private static string FindRepositoryRoot()
    {
        var candidate = new DirectoryInfo(
            Path.GetDirectoryName(
                typeof(ProductionStartupTests).Assembly.Location)!);
        while (candidate is not null)
        {
            if (File.Exists(
                    Path.Combine(candidate.FullName, "Inventory.Rewrite.slnx")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the Inventory rewrite repository root.");
    }
}
