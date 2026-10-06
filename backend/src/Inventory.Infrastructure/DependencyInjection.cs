using Inventory.Application.Abstractions;
using Inventory.Application.Attachments;
using Inventory.Application.Common;
using Inventory.Application.Dashboard;
using Inventory.Application.Identity;
using Inventory.Application.Labels;
using Inventory.Application.Security;
using Inventory.Application.Stocktakes;
using Inventory.Infrastructure.Agents;
using Inventory.Infrastructure.Attachments;
using Inventory.Infrastructure.Configuration;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Identity;
using Inventory.Infrastructure.Labels;
using Inventory.Infrastructure.Observability;
using Inventory.Infrastructure.Operations;
using Inventory.Infrastructure.Qr;
using Inventory.Infrastructure.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Inventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("InventoryDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:InventoryDatabase is required.");
        }

        var databaseOptions =
            configuration
                .GetSection(DatabaseOptions.SectionName)
                .Get<DatabaseOptions>()
            ?? new DatabaseOptions();
        services.AddSingleton(databaseOptions);

        var databasePath = (string?)null;
        string preparedConnectionString;
        if (databaseOptions.Provider == InventoryDatabaseProvider.Postgres)
        {
            preparedConnectionString =
                PostgresDatabaseConfiguration.PrepareConnectionString(
                    connectionString,
                    databaseOptions.PasswordFile);
        }
        else
        {
            preparedConnectionString =
                SqliteDatabaseConfiguration.PrepareConnectionString(
                    connectionString,
                    environment.ContentRootPath,
                    environment.IsProduction());
            databasePath =
                SqliteDatabaseConfiguration.GetPersistentDatabasePath(
                    preparedConnectionString);
        }

        services.AddSingleton(new DatabaseStorageLocation(databasePath));
        services.AddSingleton<IStorageVolumeProbe, StorageVolumeProbe>();
        services.AddSingleton<IDatabaseStorageWriteGuard, DatabaseStorageWriteGuard>();

        services.AddDbContext<InventoryDbContext>(options =>
        {
            if (databaseOptions.Provider == InventoryDatabaseProvider.Postgres)
            {
                options.UseNpgsql(
                    preparedConnectionString,
                    postgres => postgres.MigrationsAssembly(
                        PostgresDatabaseConfiguration.MigrationsAssemblyName));
                return;
            }

            options.UseSqlite(
                preparedConnectionString,
                sqlite =>
                    sqlite.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName));
        });

        services.AddOptions<SchoolFeatureOptions>()
            .Bind(configuration.GetSection("School"))
            .Validate(
                options =>
                    Uri.TryCreate(
                        options.QrBaseUrl,
                        UriKind.Absolute,
                        out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps,
                "School:QrBaseUrl must be an absolute HTTPS URL.")
            .Validate(
                options => options.WarrantyWarningDays is >= 1 and <= 365,
                "School:WarrantyWarningDays must be between 1 and 365.")
            .Validate(
                options => IsValidTimeZone(options.TimeZone),
                "School:TimeZone must identify an installed system time zone.")
            .ValidateOnStart();

        services.AddOptions<MfaSecurityOptions>()
            .Bind(configuration.GetSection("Mfa"))
            .Validate(
                options =>
                {
                    options.Validate();
                    return true;
                },
                "MFA configuration is invalid.")
            .Validate(
                options =>
                    !environment.IsProduction() ||
                    options.EnforceRequiredRoles,
                "Mfa:EnforceRequiredRoles must remain enabled in Production.")
            .Validate(
                options =>
                    !environment.IsProduction() ||
                    options.RequiredRoles.Contains(
                        RoleNames.Administrator,
                        StringComparer.Ordinal),
                "Mfa:RequiredRoles must include Administrator in Production.")
            .ValidateOnStart();

        services.AddOptions<ImportPreviewOptions>()
            .Bind(configuration.GetSection(ImportPreviewOptions.SectionName))
            .Validate(
                options =>
                {
                    options.Validate();
                    return true;
                },
                "Import preview configuration is invalid.")
            .ValidateOnStart();

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection("Storage"))
            .Validate(
                options =>
                {
                    options.Validate();
                    return true;
                },
                "Storage configuration is invalid.")
            .ValidateOnStart();

        services.AddOptions<AttachmentScanningOptions>()
            .Bind(configuration.GetSection("AttachmentScanning"))
            .Validate(
                options =>
                {
                    options.Validate();
                    return true;
                },
                "Attachment scanning configuration is invalid.")
            .Validate(
                options => !environment.IsProduction() || options.Enabled,
                "AttachmentScanning:Enabled must remain true in Production.")
            .ValidateOnStart();

        services.AddOptions<ObservabilityOptions>()
            .Bind(configuration.GetSection("Observability"))
            .Validate(
                options =>
                {
                    options.Validate();
                    return true;
                },
                "Observability configuration is invalid.")
            .ValidateOnStart();

        services.AddOptions<AgentInstallerOptions>()
            .Bind(configuration.GetSection("AgentInstaller"))
            .Validate(
                options =>
                {
                    options.Validate();
                    return true;
                },
                "Agent installer configuration is invalid.")
            .ValidateOnStart();

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddEntityFrameworkStores<InventoryDbContext>()
            .AddUserStore<ProtectedApplicationUserStore>()
            .AddDefaultTokenProviders();

        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.Zero);

        services.AddScoped<PendingMfaChallengeStore>();

        services.AddScoped<
            IUserClaimsPrincipalFactory<ApplicationUser>,
            ApplicationUserClaimsPrincipalFactory>();
        services.AddScoped<IAuditLogRepository, EfAuditLogRepository>();
        services.AddScoped<IAgentRepository, EfAgentRepository>();
        services.AddSingleton<
            IAgentInstallerArtifactProvider,
            AgentInstallerArtifactProvider>();
        services.AddScoped<IInventoryRepository, EfInventoryRepository>();
        services.AddScoped<ILocationGraphMutationRunner, EfLocationGraphMutationRunner>();
        services.AddScoped<ICustodyRepository, EfCustodyRepository>();
        services.AddScoped<IAssetOperationsRepository, EfAssetOperationsRepository>();
        services.AddScoped<IDashboardRepository, EfDashboardRepository>();
        services.AddScoped<IStocktakeRepository, EfStocktakeRepository>();
        services.AddScoped<ILeaverRepository, EfLeaverRepository>();
        services.AddScoped<IAttachmentRepository, EfAttachmentRepository>();
        services.AddSingleton<IDefenderScanProcessRunner, DefenderScanProcessRunner>();
        var attachmentScanningOptions =
            configuration
                .GetSection("AttachmentScanning")
                .Get<AttachmentScanningOptions>()
            ?? new AttachmentScanningOptions();
        if (attachmentScanningOptions.Provider == AttachmentScannerProvider.ClamAv)
        {
            services.AddSingleton<
                IAttachmentMalwareScanner,
                ClamAvAttachmentMalwareScanner>();
        }
        else
        {
            services.AddSingleton<
                IAttachmentMalwareScanner,
                WindowsDefenderAttachmentMalwareScanner>();
        }
        services.AddScoped<ISavedSearchRepository, EfSavedSearchRepository>();
        services.AddScoped<IQrRepository, EfQrRepository>();
        services.AddScoped<IWarrantyRepository, EfWarrantyRepository>();
        services.AddScoped<IImportExportRepository, EfImportExportRepository>();
        services.AddScoped<EfImportPreviewBudget>();
        services.AddScoped<IImportPreviewBudget>(provider =>
            provider.GetRequiredService<EfImportPreviewBudget>());
        services.AddHostedService<ImportPreviewCleanupService>();
        services.AddScoped<
            IReferenceDataExchangeRepository,
            EfReferenceDataExchangeRepository>();
        services.AddScoped<ISchoolSettingsProvider, EfSchoolSettingsProvider>();
        services.AddScoped<ISchoolSettingsRepository, EfSchoolSettingsRepository>();
        services.AddScoped<
            ILegacyReconciliationRepository,
            EfLegacyReconciliationRepository>();
        services.AddSingleton<IQrTokenProtector, DataProtectionQrTokenProtector>();
        services.AddSingleton<IQrCodeRenderer, QrCodeRenderer>();
        services.AddSingleton<IAssetLabelPdfRenderer, PdfAssetLabelRenderer>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IRolePermissionSeeder, RolePermissionSeeder>();
        services.AddScoped<IAdminBootstrapper, AdminBootstrapper>();
        services.AddSingleton<IdentityAdministrationLock>();
        services.AddSingleton<IOperationalMetrics, OperationalMetrics>();
        services.AddSingleton<OperationalStatusService>();
        if (databaseOptions.Provider == InventoryDatabaseProvider.Postgres)
        {
            services.AddScoped<
                IBackupRestoreService,
                PostgresExternalBackupRestoreService>();
        }
        else
        {
            services.AddScoped<IBackupRestoreService, BackupRestoreService>();
        }
        services.AddSingleton<IClock, SystemClock>();
        services.AddHttpContextAccessor();

        return services;
    }

    private static bool IsValidTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
