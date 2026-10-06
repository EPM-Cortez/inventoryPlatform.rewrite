using Inventory.Application.Common;
using Inventory.Application.Attachments;
using Inventory.Application.Identity;
using Inventory.Application.Security;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Identity;
using Inventory.Infrastructure.Observability;
using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.IntegrationTests;

public sealed class InventoryApiFactory : WebApplicationFactory<Program>
{
    public const string AdministratorEmail = "admin@inventory.test";
    public const string AdministratorPassword = "Admin-Test-Password!42";
    public const string InventoryManagerEmail = "manager@inventory.test";
    public const string InventoryManagerPassword = "Manager-Test-Password!42";
    public const string AuditorEmail = "auditor@inventory.test";
    public const string AuditorPassword = "Auditor-Test-Password!42";
    public const string TechnicianEmail = "technician@inventory.test";
    public const string TechnicianPassword = "Technician-Test-Password!42";

    private readonly SqliteConnection? _connection;
    private readonly string? _postgresConnectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly bool _seedDefaultUsers;
    private readonly bool _enforceMfa;
    private readonly int _auditMaxExportRows;
    private readonly int _failedLoginAlertThreshold;
    private readonly int _passwordOperationPermitLimit;
    private readonly int _mfaSelfServicePermitLimit;
    private readonly int _importPreviewPermitLimit;
    private readonly int _importPreviewMaximumJobs;
    private readonly long _importPreviewMaximumBytesPerUser;
    private readonly long _importPreviewMaximumBytesOverall;
    private readonly int _attachmentUploadPermitLimit;
    private readonly DateTimeOffset? _utcNow;
    private readonly FixedClock? _testClock;
    private readonly IAttachmentMalwareScanner _attachmentMalwareScanner;
    private bool _initialized;

    public InventoryApiFactory()
        : this(
            seedDefaultUsers: true,
            enforceMfa: false,
            auditMaxExportRows: 10_000,
            failedLoginAlertThreshold: 20,
            passwordOperationPermitLimit: 1_000,
            mfaSelfServicePermitLimit: 1_000,
            importPreviewPermitLimit: 1_000,
            attachmentUploadPermitLimit: 1_000,
            utcNow: null,
            attachmentMalwareScanner: null,
            postgresConnectionString: null)
    {
    }

    internal InventoryApiFactory(
        bool seedDefaultUsers,
        bool enforceMfa = false,
        int auditMaxExportRows = 10_000,
        int failedLoginAlertThreshold = 20,
        int passwordOperationPermitLimit = 1_000,
        int mfaSelfServicePermitLimit = 1_000,
        int importPreviewPermitLimit = 1_000,
        int attachmentUploadPermitLimit = 1_000,
        DateTimeOffset? utcNow = null,
        IAttachmentMalwareScanner? attachmentMalwareScanner = null,
        string? postgresConnectionString = null,
        int importPreviewMaximumJobs = 100,
        long importPreviewMaximumBytesPerUser = 64L * 1024 * 1024,
        long importPreviewMaximumBytesOverall = 512L * 1024 * 1024)
    {
        _seedDefaultUsers = seedDefaultUsers;
        _enforceMfa = enforceMfa;
        _auditMaxExportRows = auditMaxExportRows;
        _failedLoginAlertThreshold = failedLoginAlertThreshold;
        _passwordOperationPermitLimit = passwordOperationPermitLimit;
        _mfaSelfServicePermitLimit = mfaSelfServicePermitLimit;
        _importPreviewPermitLimit = importPreviewPermitLimit;
        _importPreviewMaximumJobs = importPreviewMaximumJobs;
        _importPreviewMaximumBytesPerUser = importPreviewMaximumBytesPerUser;
        _importPreviewMaximumBytesOverall = importPreviewMaximumBytesOverall;
        _attachmentUploadPermitLimit = attachmentUploadPermitLimit;
        _utcNow = utcNow;
        _testClock = utcNow.HasValue ? new FixedClock(utcNow.Value) : null;
        _attachmentMalwareScanner =
            attachmentMalwareScanner ?? new CleanAttachmentMalwareScanner();
        _postgresConnectionString = postgresConnectionString;
        _connection = postgresConnectionString is null
            ? new SqliteConnection("Data Source=:memory:")
            : null;
    }

    public async Task<DatabaseState> GetDatabaseStateAsync()
    {
        await EnsureInitializedAsync();
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var foreignKeysEnabled = true;
        if (dbContext.Database.IsSqlite())
        {
            var connection = dbContext.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys;";
            var foreignKeys = await command.ExecuteScalarAsync();
            foreignKeysEnabled =
                Convert.ToInt32(foreignKeys, CultureInfo.InvariantCulture) == 1;
        }

        return new DatabaseState(
            dbContext.Database.ProviderName,
            (await dbContext.Database.GetAppliedMigrationsAsync()).ToArray(),
            (await dbContext.Database.GetPendingMigrationsAsync()).ToArray(),
            foreignKeysEnabled);
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(
        string email,
        string password)
    {
        await EnsureInitializedAsync();
        var client = CreateHttpsClient();
        var csrf = await ApiTestClient.GetCsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                email,
                password,
                rememberMe = false,
            });
        response.EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var authenticatedCsrf = await ApiTestClient.GetCsrfTokenAsync(client);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", authenticatedCsrf);
        return client;
    }

    public HttpClient CreateHttpsClient() =>
        CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
            });

    public void AdvanceClock(TimeSpan duration) =>
        (_testClock ?? throw new InvalidOperationException(
            "This factory does not use a controllable clock.")).Advance(duration);

    public async Task DisableUserAsync(string email)
    {
        await EnsureInitializedAsync();
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"Test user {email} was not found.");
        user.IsEnabled = false;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }

    public async Task BootstrapAdministratorAsync(
        string email,
        string displayName,
        string password)
    {
        await EnsureInitializedAsync();
        await using var scope = Services.CreateAsyncScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<IAdminBootstrapper>();
        await bootstrapper.BootstrapAsync(
            new BootstrapAdminCommand(email, displayName, password),
            CancellationToken.None);
    }

    public async Task CreateUserWithCustomRoleAsync(
        string email,
        string password,
        string roleName,
        IReadOnlyCollection<string> permissions)
    {
        await EnsureInitializedAsync();
        await using var scope = Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (await roleManager.FindByNameAsync(roleName) is null)
        {
            var role = new IdentityRole<Guid>(roleName);
            var roleResult = await roleManager.CreateAsync(role);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join("; ", roleResult.Errors.Select(error => error.Description)));
            }

            foreach (var permission in permissions)
            {
                var claimResult = await roleManager.AddClaimAsync(
                    role,
                    new System.Security.Claims.Claim(Permissions.ClaimType, permission));
                if (!claimResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        string.Join(
                            "; ",
                            claimResult.Errors.Select(error => error.Description)));
                }
            }
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await CreateUserAsync(userManager, email, password, $"{roleName} user", roleName);
    }

    public async Task<IReadOnlyList<string>> GetUserAuditActionsAsync(Guid userId)
    {
        await EnsureInitializedAsync();
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await dbContext.AuditLogs
            .AsNoTracking()
            .Where(
                audit =>
                    audit.TargetType == "user" &&
                    audit.TargetId == userId.ToString())
            .OrderBy(audit => audit.OccurredAtUtc)
            .Select(audit => audit.Action)
            .ToArrayAsync();
    }

    public async Task ExecuteSqlAsync(string sql)
    {
        await EnsureInitializedAsync();
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(sql);
    }

    public async Task<int> CountAuditActionsAsync(string action)
    {
        await EnsureInitializedAsync();
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await dbContext.AuditLogs.CountAsync(audit => audit.Action == action);
    }

    public async Task<IReadOnlyList<string>> GetUserTokenValuesAsync(string email)
    {
        await EnsureInitializedAsync();
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"Test user {email} was not found.");
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await dbContext.UserTokens
            .AsNoTracking()
            .Where(token => token.UserId == user.Id)
            .Select(token => token.Value ?? string.Empty)
            .ToArrayAsync();
    }

    public async Task<IReadOnlyList<string>> GetAuditDetailsAsync(string action)
    {
        await EnsureInitializedAsync();
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await dbContext.AuditLogs
            .AsNoTracking()
            .Where(audit => audit.Action == action)
            .Select(audit => audit.DetailsJson ?? string.Empty)
            .ToArrayAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        if (_postgresConnectionString is not null)
        {
            builder.UseSetting("Database:Provider", "Postgres");
            builder.UseSetting(
                "ConnectionStrings:InventoryDatabase",
                _postgresConnectionString);
        }
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSimpleConsole(
                options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                });
        });
        builder.UseSetting("RateLimits:AuthenticationPermitLimit", "1000");
        builder.UseSetting("RateLimits:BulkPreviewPermitLimit", "1000");
        builder.UseSetting("RateLimits:StocktakeStartPermitLimit", "1000");
        builder.UseSetting(
            "RateLimits:AttachmentUploadPermitLimit",
            _attachmentUploadPermitLimit.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting(
            "RateLimits:PasswordOperationPermitLimit",
            _passwordOperationPermitLimit.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting(
            "RateLimits:MfaSelfServicePermitLimit",
            _mfaSelfServicePermitLimit.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting(
            "RateLimits:ImportPreviewPermitLimit",
            _importPreviewPermitLimit.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting(
            "ImportPreviews:MaximumActiveJobsPerUser",
            _importPreviewMaximumJobs.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting(
            "ImportPreviews:MaximumBytesPerUser",
            _importPreviewMaximumBytesPerUser.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting(
            "ImportPreviews:MaximumBytesOverall",
            _importPreviewMaximumBytesOverall.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting(
            "Mfa:EnforceRequiredRoles",
            _enforceMfa ? bool.TrueString : bool.FalseString);
        builder.UseSetting(
            "Audit:MaxExportRows",
            _auditMaxExportRows.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting(
            "Observability:FailedLoginAlertThreshold",
            _failedLoginAlertThreshold.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting(
            "Identity:ReconcileRolePresetsOnStartup",
            bool.FalseString);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(
                new EphemeralDataProtectionProvider());
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<InventoryDbContext>>();
            services.RemoveAll<InventoryDbContext>();
            services.RemoveAll<DatabaseStorageLocation>();
            services.AddSingleton(new DatabaseStorageLocation(null));
            services.RemoveAll<IAttachmentMalwareScanner>();
            services.AddSingleton(_attachmentMalwareScanner);
            if (_testClock is not null)
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_testClock);
            }

            services.AddDbContext<InventoryDbContext>(
                options =>
                {
                    if (_postgresConnectionString is not null)
                    {
                        options.UseNpgsql(
                            _postgresConnectionString,
                            postgres => postgres.MigrationsAssembly(
                                PostgresDatabaseConfiguration
                                    .MigrationsAssemblyName));
                        return;
                    }

                    options.UseSqlite(
                        _connection ??
                        throw new InvalidOperationException(
                            "The SQLite test connection is unavailable."));
                });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _initializationLock.Dispose();
            _connection?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            if (_connection is not null)
            {
                await _connection.OpenAsync();
            }
            await using var scope = Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await dbContext.Database.MigrateAsync();

            var seeder = scope.ServiceProvider.GetRequiredService<IRolePermissionSeeder>();
            await seeder.SeedAsync(CancellationToken.None);

            if (_seedDefaultUsers)
            {
                var userManager =
                    scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                await CreateUserAsync(
                    userManager,
                    AdministratorEmail,
                    AdministratorPassword,
                    "Integration Administrator",
                    RoleNames.Administrator);
                await CreateUserAsync(
                    userManager,
                    InventoryManagerEmail,
                    InventoryManagerPassword,
                    "Integration Inventory Manager",
                    RoleNames.InventoryManager);
                await CreateUserAsync(
                    userManager,
                    AuditorEmail,
                    AuditorPassword,
                    "Integration Auditor",
                    RoleNames.ReadOnlyAuditor);
                await CreateUserAsync(
                    userManager,
                    TechnicianEmail,
                    TechnicianPassword,
                    "Integration Technician",
                    RoleNames.Technician);
            }

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static async Task CreateUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        string displayName,
        string role)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            IsEnabled = true,
            MustChangePassword = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        result = await userManager.AddToRoleAsync(user, role);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }

    public sealed record DatabaseState(
        string? ProviderName,
        IReadOnlyList<string> AppliedMigrations,
        IReadOnlyList<string> PendingMigrations,
        bool ForeignKeysEnabled);

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);

        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    private sealed class CleanAttachmentMalwareScanner
        : IAttachmentMalwareScanner
    {
        public Task<AttachmentScannerReadiness> CheckReadinessAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new AttachmentScannerReadiness(
                    true,
                    "Integration test scanner",
                    "ready"));

        public Task<AttachmentScanResult> ScanAsync(
            string fileName,
            string contentType,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new AttachmentScanResult(
                    AttachmentScanVerdict.Clean,
                    "Integration test scanner",
                    "clean"));
    }
}
