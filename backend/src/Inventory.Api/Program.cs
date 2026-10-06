using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Inventory.Api.Binding;
using Inventory.Api.Errors;
using Inventory.Api.Health;
using Inventory.Api.OpenApi;
using Inventory.Api.Security;
using Inventory.Application.Assets;
using Inventory.Application.AssetTags;
using Inventory.Application.Agents;
using Inventory.Application.Assignments;
using Inventory.Application.Attachments;
using Inventory.Application.Auditing;
using Inventory.Application.BulkChanges;
using Inventory.Application.Bundles;
using Inventory.Application.Common;
using Inventory.Application.Dashboard;
using Inventory.Application.Identity;
using Inventory.Application.DataExchange;
using Inventory.Application.Labels;
using Inventory.Application.Leavers;
using Inventory.Application.References;
using Inventory.Application.People;
using Inventory.Application.Qr;
using Inventory.Application.SavedSearches;
using Inventory.Application.Security;
using Inventory.Application.School;
using Inventory.Application.Stocktakes;
using Inventory.Application.Warranties;
using Inventory.Domain.Assets;
using Inventory.Infrastructure;
using Inventory.Infrastructure.Configuration;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Operations;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    // A first migration deliberately probes for the history table. Keep this
    // one-shot command independent of Windows Event Log permissions so it can
    // run under a non-admin service account as well as in a Linux container.
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options =>
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffK ");
}

ValidateProductionConfiguration(builder);

var importExportSettings =
    builder.Configuration.GetSection("ImportExport").Get<ImportExportSettings>()
    ?? new ImportExportSettings();
importExportSettings.Validate();
var auditExportSettings =
    builder.Configuration.GetSection("Audit").Get<AuditExportSettings>()
    ?? new AuditExportSettings();
auditExportSettings.Validate();
var legacyReconciliationSettings =
    builder.Configuration
        .GetSection("LegacyReconciliation")
        .Get<LegacyReconciliationSettings>()
    ?? new LegacyReconciliationSettings();
legacyReconciliationSettings.Validate();
var agentSecuritySettings =
    builder.Configuration.GetSection("Agents").Get<AgentSecuritySettings>()
    ?? new AgentSecuritySettings();
agentSecuritySettings.Validate();

builder.Services.AddInventoryInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<PendingMfaCookieEvents>();
builder.Services.Configure<CookieAuthenticationOptions>(
    IdentityConstants.TwoFactorUserIdScheme,
    options =>
    {
        options.EventsType = typeof(PendingMfaCookieEvents);
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = false;
    });
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<AssetTagPolicyService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddScoped<AuditQueryService>();
builder.Services.AddScoped<AssignmentService>();
builder.Services.AddScoped<AttachmentService>();
builder.Services.AddScoped<AssetBulkChangeService>();
builder.Services.AddScoped<AssetBundleService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<StocktakeService>();
builder.Services.AddScoped<LeaverService>();
builder.Services.AddScoped<SavedSearchService>();
builder.Services.AddScoped<PeopleService>();
builder.Services.AddScoped<DepartmentService>();
builder.Services.AddScoped<ReferenceDataService>();
builder.Services.AddScoped<QrService>();
builder.Services.AddScoped<LabelService>();
builder.Services.AddScoped<WarrantyService>();
builder.Services.AddScoped<ImportExportService>();
builder.Services.AddScoped<ReferenceDataExchangeService>();
builder.Services.AddScoped<SchoolSettingsService>();
builder.Services.AddScoped<LegacyReconciliationService>();
builder.Services.AddSingleton(importExportSettings);
builder.Services.AddSingleton(auditExportSettings);
builder.Services.AddSingleton(legacyReconciliationSettings);
builder.Services.AddSingleton(agentSecuritySettings);
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<ApiAntiforgeryFilter>();
builder.Services.Configure<FormOptions>(
    options =>
        options.MultipartBodyLengthLimit =
            checked(
                Math.Max(
                    importExportSettings.MaxImportBytes,
                    AssetAttachment.MaximumContentBytes) +
                1024 * 1024));

builder.Services
    .AddControllers(options =>
    {
        options.Filters.AddService<ApiAntiforgeryFilter>();
        options.ModelBinderProviders.Insert(
            0,
            new StringEnumModelBinderProvider());
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<InventoryExceptionHandler>();
builder.Services.AddOpenApi(
    "v1",
    options =>
    {
        options.AddSchemaTransformer<InventoryStringEnumSchemaTransformer>();
        options.AddOperationTransformer<InventoryOpenApiOperationTransformer>();
        options.AddDocumentTransformer<InventoryOpenApiDocumentTransformer>();
    });

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-Inventory.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "__Host-Inventory.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
        ApiProblemResponses.WriteAsync(
                context.HttpContext,
                StatusCodes.Status401Unauthorized,
                "https://inventory.local/problems/authentication-required",
                "Authentication is required.",
                "Sign in with an enabled inventory account and retry the request.")
            .AsTask();
    options.Events.OnRedirectToAccessDenied = context =>
        ApiProblemResponses.WriteAsync(
                context.HttpContext,
                StatusCodes.Status403Forbidden,
                "https://inventory.local/problems/permission-denied",
                "Permission denied.",
                "The signed-in account does not have the required permission.")
            .AsTask();
});

var dataProtection = builder.Services
    .AddDataProtection()
    .SetApplicationName("Inventory.Rewrite");
var keyPath = builder.Configuration["Security:DataProtectionKeysPath"];
if (!string.IsNullOrWhiteSpace(keyPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    var protectKeysWithDpapi =
        builder.Configuration.GetValue<bool?>("Security:ProtectKeysWithDpapi") ?? true;
    var certificatePath =
        builder.Configuration["Security:DataProtectionCertificatePath"];
    if (!string.IsNullOrWhiteSpace(certificatePath))
    {
        var certificatePassword = ReadSecretOrValue(
            builder.Configuration[
                "Security:DataProtectionCertificatePasswordFile"],
            builder.Configuration[
                "Security:DataProtectionCertificatePassword"],
            "Security:DataProtectionCertificatePasswordFile");
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            certificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
        dataProtection.ProtectKeysWithCertificate(certificate);
    }
    else if (protectKeysWithDpapi && OperatingSystem.IsWindows())
    {
        dataProtection.ProtectKeysWithDpapi();
    }
}

builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(
            permission,
            policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission)));
    }
});

var forwardedHeadersConfigured = ConfigureForwardedHeaders(builder);

var authenticationPermitLimit =
    builder.Configuration.GetValue<int?>("RateLimits:AuthenticationPermitLimit") ?? 10;
var agentEnrollmentPermitLimit =
    builder.Configuration.GetValue<int?>("RateLimits:AgentEnrollmentPermitLimit") ?? 30;
var agentCheckInPermitLimit =
    builder.Configuration.GetValue<int?>("RateLimits:AgentCheckInPermitLimit") ?? 600;
var qrResolutionPermitLimit =
    builder.Configuration.GetValue<int?>("RateLimits:QrResolutionPermitLimit") ?? 120;
var readinessPermitLimit =
    builder.Configuration.GetValue<int?>("RateLimits:ReadinessPermitLimit") ?? 30;
var passwordOperationPermitLimit =
    builder.Configuration.GetValue<int?>("RateLimits:PasswordOperationPermitLimit") ?? 10;
var mfaSelfServicePermitLimit =
    builder.Configuration.GetValue<int?>("RateLimits:MfaSelfServicePermitLimit") ?? 5;
var importPreviewPermitLimit =
    builder.Configuration.GetValue<int?>("RateLimits:ImportPreviewPermitLimit") ?? 5;
var attachmentUploadPermitLimit =
    builder.Configuration.GetValue<int?>("RateLimits:AttachmentUploadPermitLimit") ?? 20;
var bulkPreviewPermitLimit =
    builder.Configuration.GetValue<int?>("RateLimits:BulkPreviewPermitLimit") ?? 10;
var stocktakeStartPermitLimit =
    builder.Configuration.GetValue<int?>("RateLimits:StocktakeStartPermitLimit") ?? 2;
if (authenticationPermitLimit < 1 ||
    agentEnrollmentPermitLimit < 1 ||
    agentCheckInPermitLimit < 1 ||
    qrResolutionPermitLimit < 1 ||
    readinessPermitLimit < 1 ||
    passwordOperationPermitLimit < 1 ||
    mfaSelfServicePermitLimit < 1 ||
    importPreviewPermitLimit < 1 ||
    attachmentUploadPermitLimit < 1 ||
    bulkPreviewPermitLimit < 1 ||
    stocktakeStartPermitLimit < 1)
{
    throw new InvalidOperationException("Rate-limit permit values must be positive.");
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, cancellationToken) =>
        ApiProblemResponses.WriteAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "https://inventory.local/problems/rate-limit-exceeded",
            "Too many requests.",
            "Wait before retrying this operation.",
            cancellationToken);
    options.AddPolicy(
        "authentication",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = authenticationPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    options.AddPolicy(
        "agent-enrollment",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = agentEnrollmentPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    options.AddPolicy(
        "agent-check-in",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = agentCheckInPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    options.AddPolicy(
        "qr-resolution",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = qrResolutionPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    options.AddPolicy(
        "probes",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = readinessPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    options.AddPolicy(
        "import-preview",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
                httpContext.Connection.RemoteIpAddress?.ToString() ??
                "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = importPreviewPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    options.AddPolicy(
        "mfa-self-service",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
                httpContext.Connection.RemoteIpAddress?.ToString() ??
                "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = mfaSelfServicePermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    options.AddPolicy(
        "self-service-password-change",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = passwordOperationPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    options.AddPolicy(
        "administrator-password-reset",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = passwordOperationPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    options.AddPolicy(
        "attachment-upload",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
                httpContext.Connection.RemoteIpAddress?.ToString() ??
                "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = attachmentUploadPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    options.AddPolicy(
        "bulk-preview",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
                httpContext.Connection.RemoteIpAddress?.ToString() ??
                "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = bulkPreviewPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
    options.AddPolicy(
        "stocktake-start",
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.User.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
                httpContext.Connection.RemoteIpAddress?.ToString() ??
                "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = stocktakeStartPermitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
});

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
        options.AddPolicy(
            "browser-client",
            policy => policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()));
}

builder.Services
    .AddHealthChecks()
    .AddCheck<DatabaseReadinessHealthCheck>("database", tags: ["ready"])
    .AddCheck<StorageReadinessHealthCheck>("storage", tags: ["ready"])
    .AddCheck<AttachmentScannerReadinessHealthCheck>(
        "attachment-scanner",
        tags: ["ready"]);

var app = builder.Build();

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await ApplyDatabaseMigrationsAsync(app);
    return;
}

if (args.Any(
        argument => argument is "--seed-admin" or "--bootstrap-admin"))
{
    await BootstrapAdministratorAsync(app);
    return;
}

if (args.Contains("--seed-roles", StringComparer.Ordinal))
{
    await SeedRolePresetsAsync(app);
    return;
}

if (args.Contains("--backup", StringComparer.Ordinal))
{
    await CreateBackupAsync(app);
    return;
}

if (args.Contains("--rehearse-restore", StringComparer.Ordinal))
{
    await RehearseRestoreAsync(app);
    return;
}

if (app.Configuration.GetValue<bool?>(
        "Identity:ReconcileRolePresetsOnStartup") ?? true)
{
    await ReconcileRolePresetsForServingAsync(app);
}

if (forwardedHeadersConfigured)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

if (allowedOrigins.Length > 0)
{
    app.UseCors("browser-client");
}

app.UseAuthentication();
app.UseMiddleware<EnabledUserValidationMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks(
        "/health/live",
        new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks(
        "/health/ready",
        new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        })
    .AllowAnonymous()
    .RequireRateLimiting("probes");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
}

app.Run();

static bool ConfigureForwardedHeaders(WebApplicationBuilder builder)
{
    var enabled =
        builder.Configuration.GetValue<bool?>("ForwardedHeaders:Enabled") ?? false;
    var knownProxies = builder.Configuration
        .GetSection("ForwardedHeaders:KnownProxies")
        .Get<string[]>()
        ?? [];
    var knownNetworks = builder.Configuration
        .GetSection("ForwardedHeaders:KnownNetworks")
        .Get<string[]>()
        ?? [];
    if (!enabled)
    {
        if (knownProxies.Length > 0 || knownNetworks.Length > 0)
        {
            throw new InvalidOperationException(
                "ForwardedHeaders:Enabled must be true when trusted proxies or networks are configured.");
        }

        return false;
    }

    if (knownProxies.Length == 0 && knownNetworks.Length == 0)
    {
        throw new InvalidOperationException(
            "ForwardedHeaders:Enabled requires at least one KnownProxies or KnownNetworks entry.");
    }

    var forwardLimit =
        builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1;
    if (forwardLimit < 1)
    {
        throw new InvalidOperationException(
            "ForwardedHeaders:ForwardLimit must be at least 1.");
    }

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = forwardLimit;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in knownProxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownProxies entry '{proxy}' is not a valid IP address.");
            }

            options.KnownProxies.Add(address);
        }

        foreach (var network in knownNetworks)
        {
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownNetworks entry '{network}' is not valid CIDR notation.");
            }

            options.KnownIPNetworks.Add(parsed);
        }
    });

    return true;
}

static void ValidateProductionConfiguration(WebApplicationBuilder builder)
{
    if (!builder.Environment.IsProduction())
    {
        return;
    }

    var keyPath = builder.Configuration["Security:DataProtectionKeysPath"];
    if (string.IsNullOrWhiteSpace(keyPath))
    {
        throw new InvalidOperationException(
            "Security:DataProtectionKeysPath is required in Production.");
    }

    var normalizedKeyPath = ProductionPathValidator.PrepareDurableDirectory(
        keyPath,
        builder.Environment.ContentRootPath,
        "Security:DataProtectionKeysPath");
    var backupPath = ProductionPathValidator.PrepareDurableDirectory(
        builder.Configuration["Storage:BackupPath"],
        builder.Environment.ContentRootPath,
        "Storage:BackupPath");
    EnsureSeparateStoragePaths(normalizedKeyPath, backupPath);

    var certificatePath =
        builder.Configuration["Security:DataProtectionCertificatePath"];
    var protectKeysWithDpapi =
        builder.Configuration.GetValue<bool?>("Security:ProtectKeysWithDpapi") ?? true;
    if (string.IsNullOrWhiteSpace(certificatePath))
    {
        if (!OperatingSystem.IsWindows() || !protectKeysWithDpapi)
        {
            throw new InvalidOperationException(
                "Production data-protection keys require Windows DPAPI or " +
                "Security:DataProtectionCertificatePath.");
        }
    }
    else
    {
        _ = ProductionPathValidator.RequireDurableExistingFile(
            certificatePath,
            builder.Environment.ContentRootPath,
            "Security:DataProtectionCertificatePath");
    }

    var allowedHosts = (builder.Configuration["AllowedHosts"] ?? string.Empty)
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (allowedHosts.Length == 0 ||
        allowedHosts.Any(host => host.Contains('*')) ||
        allowedHosts.All(IsLoopbackHost))
    {
        throw new InvalidOperationException(
            "AllowedHosts must list the real service host name(s) in Production; " +
            "wildcards and loopback-only values are not accepted.");
    }

    var origins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>()
        ?? [];
    if (origins.Any(origin => origin.Contains('*')))
    {
        throw new InvalidOperationException(
            "Cors:AllowedOrigins cannot contain wildcards in Production.");
    }

    if (!(builder.Configuration.GetValue<bool?>(
            "Identity:ReconcileRolePresetsOnStartup") ?? true))
    {
        throw new InvalidOperationException(
            "Identity:ReconcileRolePresetsOnStartup must remain enabled in Production.");
    }

    if (!(builder.Configuration.GetValue<bool?>(
            "AttachmentScanning:Enabled") ?? true))
    {
        throw new InvalidOperationException(
            "AttachmentScanning:Enabled must remain true in Production.");
    }

    var mfaOptions = builder.Configuration
        .GetSection("Mfa")
        .Get<MfaSecurityOptions>()
        ?? new MfaSecurityOptions();
    if (!mfaOptions.EnforceRequiredRoles)
    {
        throw new InvalidOperationException(
            "Mfa:EnforceRequiredRoles must remain enabled in Production.");
    }

    if (!mfaOptions.RequiredRoles.Contains(
            RoleNames.Administrator,
            StringComparer.Ordinal))
    {
        throw new InvalidOperationException(
            "Mfa:RequiredRoles must include Administrator in Production.");
    }

    var qrBaseUrl = builder.Configuration["School:QrBaseUrl"];
    if (!Uri.TryCreate(qrBaseUrl, UriKind.Absolute, out var qrUri) ||
        qrUri.Scheme != Uri.UriSchemeHttps ||
        qrUri.IsLoopback)
    {
        throw new InvalidOperationException(
            "School:QrBaseUrl must be a non-loopback absolute HTTPS URL in Production.");
    }
}

static void EnsureSeparateStoragePaths(string keyPath, string backupPath)
{
    static bool IsWithin(string candidate, string parent)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(
                Path.GetPathRoot(candidate),
                Path.GetPathRoot(parent),
                comparison))
        {
            return false;
        }

        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "." ||
            (!relative.Equals("..", comparison) &&
             !relative.StartsWith(
                 $"..{Path.DirectorySeparatorChar}",
                 comparison));
    }

    if (IsWithin(keyPath, backupPath) || IsWithin(backupPath, keyPath))
    {
        throw new InvalidOperationException(
            "Storage:BackupPath and Security:DataProtectionKeysPath must be separate, non-nested directories.");
    }
}

static string? ReadSecretOrValue(
    string? secretFile,
    string? configuredValue,
    string configurationName)
{
    if (string.IsNullOrWhiteSpace(secretFile))
    {
        return configuredValue;
    }

    if (!Path.IsPathFullyQualified(secretFile))
    {
        throw new InvalidOperationException(
            $"{configurationName} must be an absolute path.");
    }

    var fullPath = Path.GetFullPath(secretFile);
    if (!File.Exists(fullPath))
    {
        throw new InvalidOperationException(
            $"{configurationName} must identify an existing secret file.");
    }

    var value = File.ReadAllText(fullPath).TrimEnd('\r', '\n');
    if (string.IsNullOrEmpty(value) || value.Length > 4096)
    {
        throw new InvalidOperationException(
            $"{configurationName} must contain a non-empty secret of at most 4096 characters.");
    }

    return value;
}

static bool IsLoopbackHost(string host) =>
    host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
    (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

static async Task BootstrapAdministratorAsync(WebApplication app)
{
    var email = app.Configuration["BootstrapAdmin:Email"];
    var displayName = app.Configuration["BootstrapAdmin:DisplayName"];
    var password = app.Configuration["BootstrapAdmin:Password"];

    if (string.IsNullOrWhiteSpace(email) ||
        string.IsNullOrWhiteSpace(displayName) ||
        string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException(
            "BootstrapAdmin:Email, BootstrapAdmin:DisplayName, and BootstrapAdmin:Password " +
            "must be supplied through secure configuration.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    var bootstrapper = scope.ServiceProvider.GetRequiredService<IAdminBootstrapper>();
    await bootstrapper.BootstrapAsync(
        new BootstrapAdminCommand(email, displayName, password),
        CancellationToken.None);
    Console.WriteLine($"Administrator seed completed for {email.Trim()}.");
}

static async Task ApplyDatabaseMigrationsAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await dbContext.Database.MigrateAsync(CancellationToken.None);
    Console.WriteLine(
        $"Database migrations applied using {dbContext.Database.ProviderName}.");
}

static async Task SeedRolePresetsAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<IRolePermissionSeeder>();
    await seeder.SeedAsync(CancellationToken.None);
    Console.WriteLine("Identity role preset seed completed.");
}

static async Task ReconcileRolePresetsForServingAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    if (!await dbContext.Database.CanConnectAsync(CancellationToken.None))
    {
        throw new InvalidOperationException(
            "The inventory database is unavailable. Apply migrations before starting the API.");
    }

    var pendingMigrations = await dbContext.Database
        .GetPendingMigrationsAsync(CancellationToken.None);
    if (pendingMigrations.Any())
    {
        throw new InvalidOperationException(
            "The inventory database has pending migrations. Apply migrations before starting the API.");
    }

    var seeder = scope.ServiceProvider.GetRequiredService<IRolePermissionSeeder>();
    await seeder.SeedAsync(CancellationToken.None);
}

static async Task CreateBackupAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var service = scope.ServiceProvider.GetRequiredService<IBackupRestoreService>();
    var result = await service.CreateBackupAsync(CancellationToken.None);
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                result.Manifest.BackupId,
                result.BackupDirectory,
                result.Manifest.CompletedAtUtc,
                result.Manifest.DurationMilliseconds,
                fileCount = result.Manifest.Files.Count,
            }));
}

static async Task RehearseRestoreAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var service = scope.ServiceProvider.GetRequiredService<IBackupRestoreService>();
    var result = await service.RehearseLatestRestoreAsync(CancellationToken.None);
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                result.Report.RehearsalId,
                result.Report.BackupId,
                result.Report.Status,
                result.Report.DurationMilliseconds,
                result.ReportPath,
            }));
}
