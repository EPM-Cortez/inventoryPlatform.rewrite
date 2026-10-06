using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Application.Security;
using Inventory.Infrastructure.Configuration;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Identity;
using Inventory.Infrastructure.Qr;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Operations;

public sealed record BackupFileEvidence(
    string RelativePath,
    long Length,
    string Sha256);

public sealed record InventoryBackupManifest(
    int FormatVersion,
    string BackupId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long DurationMilliseconds,
    string Release,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyDictionary<string, long> RowCounts,
    IReadOnlyList<BackupFileEvidence> Files);

public sealed record RestoreCheckEvidence(
    string Name,
    bool Passed,
    string Detail);

public sealed record RestoreRehearsalReport(
    int FormatVersion,
    string RehearsalId,
    string BackupId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long DurationMilliseconds,
    string Status,
    IReadOnlyList<RestoreCheckEvidence> Checks,
    IReadOnlyList<string> RollbackProcedure,
    string? Failure);

public sealed record BackupOperationResult(
    string BackupDirectory,
    InventoryBackupManifest Manifest);

public sealed record RestoreRehearsalResult(
    string ReportPath,
    RestoreRehearsalReport Report);

public interface IBackupRestoreService
{
    Task<BackupOperationResult> CreateBackupAsync(
        CancellationToken cancellationToken);

    Task<RestoreRehearsalResult> RehearseLatestRestoreAsync(
        CancellationToken cancellationToken);
}

public sealed partial class BackupRestoreService : IBackupRestoreService
{
    private const string DatabaseFileName = "inventory.db";
    private const string ManifestFileName = "manifest.json";
    private const string DataProtectionApplicationName = "Inventory.Rewrite";
    private const string AuthenticatorKeyTokenName = "AuthenticatorKey";
    private const string RecoveryCodesTokenName = "RecoveryCodes";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

    private static readonly string[] RollbackProcedure =
    [
        "Stop the API and prevent all writes.",
        "Preserve the current database and data-protection key directory for investigation.",
        "Verify the selected backup manifest and file hashes.",
        "Restore inventory.db and its matching key directory to the configured durable paths.",
        "Start one API instance, apply only the recorded compatible release, and confirm /health/ready.",
        "Verify administrator login, a known asset and history, audit access, and an active QR before reopening writes.",
    ];

    private readonly InventoryDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly StorageOptions _storageOptions;
    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;
    private readonly IClock _clock;
    private readonly ILogger<BackupRestoreService> _logger;

    internal Func<CancellationToken, Task>? AfterSnapshotForTesting { get; set; }

    public BackupRestoreService(
        InventoryDbContext dbContext,
        IConfiguration configuration,
        IOptions<StorageOptions> storageOptions,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IClock clock,
        ILogger<BackupRestoreService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _storageOptions = storageOptions.Value;
        _passwordHasher = passwordHasher;
        _clock = clock;
        _logger = logger;
    }

    public async Task<BackupOperationResult> CreateBackupAsync(
        CancellationToken cancellationToken)
    {
        var startedAtUtc = _clock.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var backupRoot = RequireBackupRoot();
        EnsureStorageCapacity(backupRoot);
        var keyRoot = RequireKeyRoot();
        var backupId =
            $"{startedAtUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}"[..37];
        var targetDirectory = Path.Combine(backupRoot, $"backup-{backupId}");
        var stagingDirectory = Path.Combine(backupRoot, $".staging-{backupId}");

        if (Directory.Exists(targetDirectory) || Directory.Exists(stagingDirectory))
        {
            throw new InvalidOperationException(
                "The generated backup destination already exists.");
        }

        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var backupDatabasePath = Path.Combine(
                stagingDirectory,
                DatabaseFileName);
            await CreateOnlineDatabaseSnapshotAsync(
                backupDatabasePath,
                cancellationToken);
            if (AfterSnapshotForTesting is not null)
            {
                await AfterSnapshotForTesting(cancellationToken);
            }

            var backupKeyRoot = Path.Combine(stagingDirectory, "keys");
            await CopyKeyRingAsync(keyRoot, backupKeyRoot, cancellationToken);

            await using var snapshotContext = CreateRestoredContext(
                backupDatabasePath);
            var migrations = (
                await snapshotContext.Database.GetAppliedMigrationsAsync(
                    cancellationToken))
                .ToArray();
            var rowCounts = await ReadRowCountsAsync(
                snapshotContext,
                cancellationToken);
            var fileEvidence = await CreateFileEvidenceAsync(
                stagingDirectory,
                cancellationToken);
            stopwatch.Stop();

            var manifest = new InventoryBackupManifest(
                FormatVersion: 1,
                BackupId: backupId,
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: _clock.UtcNow,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                Release: GetRelease(),
                AppliedMigrations: migrations,
                RowCounts: rowCounts,
                Files: fileEvidence);
            await WriteJsonAsync(
                Path.Combine(stagingDirectory, ManifestFileName),
                manifest,
                cancellationToken);

            Directory.Move(stagingDirectory, targetDirectory);
            LogBackupCompleted(
                _logger,
                backupId,
                manifest.DurationMilliseconds,
                manifest.Files.Count);
            return new BackupOperationResult(targetDirectory, manifest);
        }
        catch
        {
            DeleteOperationDirectory(stagingDirectory, backupRoot);
            throw;
        }
    }

    public async Task<RestoreRehearsalResult> RehearseLatestRestoreAsync(
        CancellationToken cancellationToken)
    {
        var startedAtUtc = _clock.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var backupRoot = RequireBackupRoot();
        var sourceDirectory = Directory
            .EnumerateDirectories(
                backupRoot,
                "backup-*",
                SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No completed backup is available for restore rehearsal.");
        var manifest = await ReadManifestAsync(
            sourceDirectory,
            cancellationToken);
        var rehearsalId =
            $"{startedAtUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}"[..37];
        var rehearsalDirectory = Path.Combine(
            backupRoot,
            $".rehearsal-{rehearsalId}");
        var reportDirectory = Path.Combine(backupRoot, "rehearsals");
        Directory.CreateDirectory(reportDirectory);
        var reportPath = Path.Combine(
            reportDirectory,
            $"rehearsal-{rehearsalId}.json");
        var checks = new List<RestoreCheckEvidence>();

        Directory.CreateDirectory(rehearsalDirectory);
        try
        {
            await VerifyManifestFilesAsync(
                sourceDirectory,
                manifest,
                cancellationToken);
            checks.Add(
                new RestoreCheckEvidence(
                    "manifest_hashes",
                    true,
                    $"{manifest.Files.Count} backup files matched their SHA-256 evidence."));

            await CopyDirectoryAsync(
                sourceDirectory,
                rehearsalDirectory,
                ManifestFileName,
                cancellationToken);
            var restoredDatabasePath = Path.Combine(
                rehearsalDirectory,
                DatabaseFileName);
            var restoredKeyRoot = Path.Combine(rehearsalDirectory, "keys");

            await using var restoredContext = CreateRestoredContext(
                restoredDatabasePath);
            await VerifySqliteIntegrityAsync(
                restoredContext,
                checks,
                cancellationToken);
            await VerifyMigrationsAsync(
                restoredContext,
                manifest,
                checks,
                cancellationToken);
            await VerifyRowCountsAsync(
                restoredContext,
                manifest,
                checks,
                cancellationToken);
            await VerifyAttachmentContentsAsync(
                restoredContext,
                checks,
                cancellationToken);
            await VerifyIdentityAsync(
                restoredContext,
                restoredKeyRoot,
                checks,
                cancellationToken);
            await VerifyQrAndAssetHistoryAsync(
                restoredContext,
                restoredKeyRoot,
                checks,
                cancellationToken);

            stopwatch.Stop();
            var report = CreateReport(
                rehearsalId,
                manifest.BackupId,
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                "passed",
                checks,
                failure: null);
            await WriteJsonAsync(reportPath, report, cancellationToken);
            LogRehearsalCompleted(
                _logger,
                rehearsalId,
                manifest.BackupId,
                report.DurationMilliseconds);
            return new RestoreRehearsalResult(reportPath, report);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            checks.Add(
                new RestoreCheckEvidence(
                    "rehearsal",
                    false,
                    "The isolated restore rehearsal did not complete."));
            var report = CreateReport(
                rehearsalId,
                manifest.BackupId,
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                "failed",
                checks,
                failure: exception.Message);
            await WriteJsonAsync(
                reportPath,
                report,
                CancellationToken.None);
            LogRehearsalFailed(
                _logger,
                rehearsalId,
                manifest.BackupId,
                exception);
            throw new InvalidOperationException(
                $"Restore rehearsal failed. Evidence: {reportPath}",
                exception);
        }
        finally
        {
            DeleteOperationDirectory(rehearsalDirectory, backupRoot);
        }
    }

    private async Task CreateOnlineDatabaseSnapshotAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.GetDbConnection() is not SqliteConnection source)
        {
            throw new InvalidOperationException(
                "Backup requires the configured SQLite database provider.");
        }

        var sourceBuilder = new SqliteConnectionStringBuilder(
            source.ConnectionString);
        if (sourceBuilder.Mode == SqliteOpenMode.Memory ||
            string.Equals(
                sourceBuilder.DataSource,
                ":memory:",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Backup requires a persistent SQLite database file.");
        }

        var openedSource = source.State != ConnectionState.Open;
        if (openedSource)
        {
            await source.OpenAsync(cancellationToken);
        }

        try
        {
            var destinationBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                ForeignKeys = true,
                DefaultTimeout = 30,
                Pooling = false,
            };
            await using var destination = new SqliteConnection(
                destinationBuilder.ToString());
            await destination.OpenAsync(cancellationToken);
            source.BackupDatabase(destination);
        }
        finally
        {
            if (openedSource)
            {
                await source.CloseAsync();
            }
        }
    }

    private static async Task VerifySqliteIntegrityAsync(
        InventoryDbContext restoredContext,
        List<RestoreCheckEvidence> checks,
        CancellationToken cancellationToken)
    {
        var connection = (SqliteConnection)restoredContext.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        await using var integrityCommand = connection.CreateCommand();
        integrityCommand.CommandText = "PRAGMA integrity_check;";
        var integrityResult = Convert.ToString(
            await integrityCommand.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SQLite integrity_check returned '{integrityResult}'.");
        }

        checks.Add(
            new RestoreCheckEvidence(
                "sqlite_integrity",
                true,
                "PRAGMA integrity_check returned ok."));

        await using var foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await foreignKeyCommand.ExecuteReaderAsync(
            cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "SQLite foreign_key_check reported at least one violation.");
        }

        checks.Add(
            new RestoreCheckEvidence(
                "foreign_keys",
                true,
                "PRAGMA foreign_key_check returned no violations."));
    }

    private static async Task VerifyMigrationsAsync(
        InventoryDbContext restoredContext,
        InventoryBackupManifest manifest,
        List<RestoreCheckEvidence> checks,
        CancellationToken cancellationToken)
    {
        var applied = (
            await restoredContext.Database.GetAppliedMigrationsAsync(
                cancellationToken))
            .ToArray();
        var pending = (
            await restoredContext.Database.GetPendingMigrationsAsync(
                cancellationToken))
            .ToArray();
        if (pending.Length > 0 ||
            !applied.SequenceEqual(
                manifest.AppliedMigrations,
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The restored schema does not match the backup manifest and current release.");
        }

        checks.Add(
            new RestoreCheckEvidence(
                "migrations",
                true,
                $"{applied.Length} applied migrations match; none are pending."));
    }

    private static async Task VerifyRowCountsAsync(
        InventoryDbContext restoredContext,
        InventoryBackupManifest manifest,
        List<RestoreCheckEvidence> checks,
        CancellationToken cancellationToken)
    {
        var restoredCounts = await ReadRowCountsAsync(
            restoredContext,
            cancellationToken);
        if (manifest.RowCounts.Count != restoredCounts.Count ||
            manifest.RowCounts.Any(
                pair =>
                    !restoredCounts.TryGetValue(pair.Key, out var count) ||
                    count != pair.Value))
        {
            throw new InvalidOperationException(
                "Restored source/target row counts do not match the backup manifest.");
        }

        checks.Add(
            new RestoreCheckEvidence(
                "row_counts",
                true,
                $"{restoredCounts.Count} recorded table counts matched."));
    }

    private static async Task VerifyAttachmentContentsAsync(
        InventoryDbContext restoredContext,
        List<RestoreCheckEvidence> checks,
        CancellationToken cancellationToken)
    {
        var verifiedCount = 0;
        var attachments = restoredContext.AssetAttachments
            .AsNoTracking()
            .OrderBy(attachment => attachment.Id)
            .Select(
                attachment => new
                {
                    attachment.Id,
                    attachment.Content,
                    attachment.SizeBytes,
                    attachment.Sha256,
                })
            .AsAsyncEnumerable();

        await foreach (var attachment in attachments.WithCancellation(
                           cancellationToken))
        {
            if (attachment.Content.LongLength != attachment.SizeBytes)
            {
                throw new InvalidOperationException(
                    $"Restored attachment {attachment.Id} has a content-size mismatch.");
            }

            var calculatedHash = Convert.ToHexString(
                SHA256.HashData(attachment.Content));
            if (!string.Equals(
                    calculatedHash,
                    attachment.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Restored attachment {attachment.Id} failed SHA-256 verification.");
            }

            verifiedCount++;
        }

        checks.Add(
            new RestoreCheckEvidence(
                "attachment_content_hashes",
                true,
                $"{verifiedCount} restored attachment content hash(es) matched."));
    }

    private async Task VerifyIdentityAsync(
        InventoryDbContext restoredContext,
        string restoredKeyRoot,
        List<RestoreCheckEvidence> checks,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_storageOptions.RehearsalLoginEmail) ||
            string.IsNullOrWhiteSpace(_storageOptions.RehearsalLoginPassword))
        {
            throw new InvalidOperationException(
                "Storage:RehearsalLoginEmail and Storage:RehearsalLoginPassword " +
                "are required for a restore rehearsal.");
        }

        var normalizedEmail = _storageOptions.RehearsalLoginEmail
            .Trim()
            .ToUpperInvariant();
        var user = await restoredContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.NormalizedEmail == normalizedEmail,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The configured rehearsal login was not found in the restored database.");
        if (!user.IsEnabled || user.ArchivedAtUtc.HasValue ||
            string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            throw new InvalidOperationException(
                "The configured rehearsal login is not an enabled password account.");
        }

        var passwordResult = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            _storageOptions.RehearsalLoginPassword);
        if (passwordResult == PasswordVerificationResult.Failed)
        {
            throw new InvalidOperationException(
                "The configured rehearsal login password was rejected.");
        }

        checks.Add(
            new RestoreCheckEvidence(
                "login",
                true,
                "The configured enabled account password verified against the restored Identity store."));

        var assignedRoles = await (
                from userRole in restoredContext.UserRoles.AsNoTracking()
                join role in restoredContext.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id
                where userRole.UserId == user.Id
                select new
                {
                    role.Id,
                    role.Name,
                })
            .ToArrayAsync(cancellationToken);
        var administratorRole = assignedRoles.SingleOrDefault(
            role => string.Equals(
                role.Name,
                RoleNames.Administrator,
                StringComparison.Ordinal));
        if (administratorRole is null)
        {
            throw new InvalidOperationException(
                "The configured rehearsal login does not retain the Administrator role.");
        }

        var restoredPermissions = await restoredContext.RoleClaims
            .AsNoTracking()
            .Where(
                claim =>
                    claim.RoleId == administratorRole.Id &&
                    claim.ClaimType == Permissions.ClaimType &&
                    claim.ClaimValue != null)
            .Select(claim => claim.ClaimValue!)
            .ToArrayAsync(cancellationToken);
        var expectedPermissions =
            RolePermissionPresets.All[RoleNames.Administrator];
        if (!new HashSet<string>(
                restoredPermissions,
                StringComparer.Ordinal)
            .SetEquals(expectedPermissions))
        {
            throw new InvalidOperationException(
                "The restored Administrator role permissions do not match the current preset.");
        }

        if (!user.TwoFactorEnabled)
        {
            throw new InvalidOperationException(
                "The configured rehearsal login does not retain enabled MFA.");
        }

        var protectedMfaTokens = await restoredContext.UserTokens
            .AsNoTracking()
            .Where(
                token =>
                    token.UserId == user.Id &&
                    token.Value != null &&
                    (token.Name == AuthenticatorKeyTokenName ||
                     token.Name == RecoveryCodesTokenName))
            .Select(
                token => new
                {
                    token.Name,
                    Value = token.Value!,
                })
            .ToArrayAsync(cancellationToken);
        if (!protectedMfaTokens
                .Select(token => token.Name)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(
                    [AuthenticatorKeyTokenName, RecoveryCodesTokenName]))
        {
            throw new InvalidOperationException(
                "The configured rehearsal login has incomplete MFA token material.");
        }

        var protector = CreateRestoredDataProtectionProvider(restoredKeyRoot)
            .CreateProtector(ProtectedApplicationUserStore.ProtectorPurpose);
        foreach (var token in protectedMfaTokens)
        {
            if (!token.Value.StartsWith(
                    ProtectedApplicationUserStore.ProtectedValuePrefix,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The restored MFA token material is not protected at rest.");
            }

            var unprotected = protector.Unprotect(
                token.Value[
                    ProtectedApplicationUserStore.ProtectedValuePrefix.Length..]);
            if (string.IsNullOrWhiteSpace(unprotected))
            {
                throw new InvalidOperationException(
                    "The restored key ring produced empty MFA token material.");
            }
        }

        checks.Add(
            new RestoreCheckEvidence(
                "identity_authorization_and_mfa",
                true,
                $"Administrator role, {restoredPermissions.Length} permissions, " +
                $"and {protectedMfaTokens.Length} protected MFA token records verified."));
    }

    private async Task VerifyQrAndAssetHistoryAsync(
        InventoryDbContext restoredContext,
        string restoredKeyRoot,
        List<RestoreCheckEvidence> checks,
        CancellationToken cancellationToken)
    {
        var identifier = await restoredContext.QrIdentifiers
            .AsNoTracking()
            .Where(candidate => candidate.RevokedAtUtc == null)
            .OrderBy(candidate => candidate.IssuedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (identifier is null)
        {
            if (_storageOptions.RehearsalRequiresActiveQr)
            {
                throw new InvalidOperationException(
                    "No active QR identifier is available for restore verification.");
            }

            checks.Add(
                new RestoreCheckEvidence(
                    "qr_resolution",
                    true,
                    "No active QR exists and the configured rehearsal permits an empty QR set."));
            return;
        }

        var provider = CreateRestoredDataProtectionProvider(restoredKeyRoot);
        var tokenProtector = new DataProtectionQrTokenProtector(provider);
        var token = tokenProtector.Unprotect(identifier.ProtectedToken);
        if (!string.Equals(
                tokenProtector.Hash(token),
                identifier.TokenHash,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The restored key ring could not reproduce the active QR token hash.");
        }

        var asset = await restoredContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == identifier.AssetId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The active QR identifier does not resolve to a restored asset.");
        var historyCount = await restoredContext.AssetEvents
            .AsNoTracking()
            .CountAsync(
                candidate => candidate.AssetId == asset.Id,
                cancellationToken);
        if (historyCount < 1)
        {
            throw new InvalidOperationException(
                "The representative restored asset has no immutable history.");
        }

        checks.Add(
            new RestoreCheckEvidence(
                "qr_resolution",
                true,
                "An active protected QR token was unprotected and matched its stored hash."));
        checks.Add(
            new RestoreCheckEvidence(
                "asset_history",
                true,
                $"Asset {asset.AssetTag} resolved with {historyCount} history record(s)."));

        var auditSampleExists = await restoredContext.AuditLogs
            .AsNoTracking()
            .AnyAsync(cancellationToken);
        if (!auditSampleExists)
        {
            throw new InvalidOperationException(
                "The restored database has no audit sample to verify.");
        }

        checks.Add(
            new RestoreCheckEvidence(
                "audit_sample",
                true,
                "At least one immutable audit record was readable."));
    }

    private IDataProtectionProvider CreateRestoredDataProtectionProvider(
        string restoredKeyRoot)
    {
        if (!Directory.Exists(restoredKeyRoot))
        {
            throw new InvalidOperationException(
                "The restored data-protection key directory is missing.");
        }

        return DataProtectionProvider.Create(
            new DirectoryInfo(restoredKeyRoot),
            builder =>
            {
                builder
                    .SetApplicationName(DataProtectionApplicationName)
                    .DisableAutomaticKeyGeneration();
                var certificatePath =
                    _configuration["Security:DataProtectionCertificatePath"];
                if (!string.IsNullOrWhiteSpace(certificatePath))
                {
                    var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                        certificatePath,
                        _configuration[
                            "Security:DataProtectionCertificatePassword"],
                        X509KeyStorageFlags.EphemeralKeySet);
                    builder.ProtectKeysWithCertificate(certificate);
                }
                else if (
                    OperatingSystem.IsWindows() &&
                    (_configuration.GetValue<bool?>(
                        "Security:ProtectKeysWithDpapi") ?? true))
                {
                    builder.ProtectKeysWithDpapi();
                }
            });
    }

    private static InventoryDbContext CreateRestoredContext(
        string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            DefaultTimeout = 30,
            Pooling = false,
        }.ToString();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(
                connectionString,
                sqlite =>
                    sqlite.MigrationsAssembly(
                        typeof(InventoryDbContext).Assembly.FullName))
            .Options;
        return new InventoryDbContext(options);
    }

    private static async Task<IReadOnlyDictionary<string, long>> ReadRowCountsAsync(
        InventoryDbContext context,
        CancellationToken cancellationToken)
    {
        return new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            ["assetAttachments"] =
                await context.AssetAttachments.LongCountAsync(
                    cancellationToken),
            ["assetBulkChangeJobs"] =
                await context.AssetBulkChangeJobs.LongCountAsync(
                    cancellationToken),
            ["assetBundleMembers"] =
                await context.AssetBundleMembers.LongCountAsync(
                    cancellationToken),
            ["assetBundles"] =
                await context.AssetBundles.LongCountAsync(cancellationToken),
            ["assets"] = await context.Assets.LongCountAsync(cancellationToken),
            ["assetEvents"] =
                await context.AssetEvents.LongCountAsync(cancellationToken),
            ["assetImportJobs"] =
                await context.AssetImportJobs.LongCountAsync(cancellationToken),
            ["assetTagPolicies"] =
                await context.AssetTagPolicies.LongCountAsync(
                    cancellationToken),
            ["assignments"] =
                await context.AssetAssignments.LongCountAsync(cancellationToken),
            ["auditLogs"] =
                await context.AuditLogs.LongCountAsync(cancellationToken),
            ["categories"] =
                await context.AssetCategories.LongCountAsync(cancellationToken),
            ["departments"] =
                await context.Departments.LongCountAsync(cancellationToken),
            ["locations"] =
                await context.Locations.LongCountAsync(cancellationToken),
            ["roleClaims"] =
                await context.RoleClaims.LongCountAsync(cancellationToken),
            ["roles"] = await context.Roles.LongCountAsync(cancellationToken),
            ["userClaims"] =
                await context.UserClaims.LongCountAsync(cancellationToken),
            ["userLogins"] =
                await context.UserLogins.LongCountAsync(cancellationToken),
            ["userRoles"] =
                await context.UserRoles.LongCountAsync(cancellationToken),
            ["userTokens"] =
                await context.UserTokens.LongCountAsync(cancellationToken),
            ["legacyReconciliationRuns"] =
                await context.LegacyReconciliationRuns.LongCountAsync(
                    cancellationToken),
            ["legacyReconciliationSignOffs"] =
                await context.LegacyReconciliationSignOffs.LongCountAsync(
                    cancellationToken),
            ["leaverCases"] =
                await context.LeaverCases.LongCountAsync(cancellationToken),
            ["agentEnrollmentProfiles"] =
                await context.AgentEnrollmentProfiles.LongCountAsync(
                    cancellationToken),
            ["agentKeyRecoveryAuthorizations"] =
                await context.AgentKeyRecoveryAuthorizations.LongCountAsync(
                    cancellationToken),
            ["agentRequestNonces"] =
                await context.AgentRequestNonces.LongCountAsync(
                    cancellationToken),
            ["managedAgents"] =
                await context.ManagedAgents.LongCountAsync(cancellationToken),
            ["managedAgentSnapshots"] =
                await context.ManagedAgentSnapshots.LongCountAsync(
                    cancellationToken),
            ["people"] = await context.People.LongCountAsync(cancellationToken),
            ["qrIdentifiers"] =
                await context.QrIdentifiers.LongCountAsync(cancellationToken),
            ["referenceDataImportJobs"] =
                await context.ReferenceDataImportJobs.LongCountAsync(
                    cancellationToken),
            ["savedAssetSearches"] =
                await context.SavedAssetSearches.LongCountAsync(
                    cancellationToken),
            ["schoolProfiles"] =
                await context.SchoolProfiles.LongCountAsync(cancellationToken),
            ["stocktakeExpectedAssets"] =
                await context.StocktakeExpectedAssets.LongCountAsync(
                    cancellationToken),
            ["stocktakeObservations"] =
                await context.StocktakeObservations.LongCountAsync(
                    cancellationToken),
            ["stocktakeSessions"] =
                await context.StocktakeSessions.LongCountAsync(
                    cancellationToken),
            ["users"] = await context.Users.LongCountAsync(cancellationToken),
            ["warranties"] =
                await context.WarrantyCoverages.LongCountAsync(cancellationToken),
        };
    }

    private string RequireBackupRoot()
    {
        if (string.IsNullOrWhiteSpace(_storageOptions.BackupPath) ||
            !Path.IsPathFullyQualified(_storageOptions.BackupPath))
        {
            throw new InvalidOperationException(
                "Storage:BackupPath must be an absolute directory.");
        }

        var backupRoot = Path.GetFullPath(_storageOptions.BackupPath);
        Directory.CreateDirectory(backupRoot);
        return backupRoot;
    }

    private string RequireKeyRoot()
    {
        var configuredPath =
            _configuration["Security:DataProtectionKeysPath"];
        if (string.IsNullOrWhiteSpace(configuredPath) ||
            !Path.IsPathFullyQualified(configuredPath))
        {
            throw new InvalidOperationException(
                "Security:DataProtectionKeysPath must be an absolute directory for backup.");
        }

        var keyRoot = Path.GetFullPath(configuredPath);
        if (!Directory.Exists(keyRoot) ||
            !Directory.EnumerateFiles(
                keyRoot,
                "*",
                SearchOption.AllDirectories).Any())
        {
            throw new InvalidOperationException(
                "The data-protection key directory is missing or empty.");
        }

        return keyRoot;
    }

    private void EnsureStorageCapacity(string backupRoot)
    {
        var root = Path.GetPathRoot(backupRoot)
            ?? throw new InvalidOperationException(
                "Storage:BackupPath has no filesystem root.");
        if (new DriveInfo(root).AvailableFreeSpace <
            _storageOptions.MinimumFreeSpaceBytes)
        {
            throw new InvalidOperationException(
                "Backup storage is below Storage:MinimumFreeSpaceBytes.");
        }
    }

    private static async Task CopyKeyRingAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        var files = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                "The data-protection key directory is empty.");
        }

        foreach (var sourcePath in files)
        {
            if ((File.GetAttributes(sourcePath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "Data-protection key backups do not follow reparse points.");
            }

            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            var destinationPath = SafeCombine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await CopyFileAsync(
                sourcePath,
                destinationPath,
                cancellationToken);
        }
    }

    private static async Task CopyDirectoryAsync(
        string sourceRoot,
        string destinationRoot,
        string excludedFileName,
        CancellationToken cancellationToken)
    {
        foreach (var sourcePath in Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            if (relativePath.Equals(
                    excludedFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destinationPath = SafeCombine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await CopyFileAsync(
                sourcePath,
                destinationPath,
                cancellationToken);
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<BackupFileEvidence>>
        CreateFileEvidenceAsync(
            string backupDirectory,
            CancellationToken cancellationToken)
    {
        var evidence = new List<BackupFileEvidence>();
        foreach (var path in Directory
            .EnumerateFiles(
                backupDirectory,
                "*",
                SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(
                stream,
                cancellationToken);
            evidence.Add(
                new BackupFileEvidence(
                    Path.GetRelativePath(backupDirectory, path)
                        .Replace('\\', '/'),
                    stream.Length,
                    Convert.ToHexString(hash)));
        }

        return evidence;
    }

    private static async Task VerifyManifestFilesAsync(
        string backupDirectory,
        InventoryBackupManifest manifest,
        CancellationToken cancellationToken)
    {
        if (manifest.FormatVersion != 1 ||
            string.IsNullOrWhiteSpace(manifest.BackupId) ||
            manifest.Files.Count == 0)
        {
            throw new InvalidOperationException(
                "The backup manifest format is invalid.");
        }

        foreach (var expected in manifest.Files)
        {
            var path = SafeCombine(
                backupDirectory,
                expected.RelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Backup file '{expected.RelativePath}' is missing.");
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != expected.Length)
            {
                throw new InvalidOperationException(
                    $"Backup file '{expected.RelativePath}' has the wrong length.");
            }

            var hash = Convert.ToHexString(
                await SHA256.HashDataAsync(
                    stream,
                    cancellationToken));
            if (!string.Equals(
                    hash,
                    expected.Sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Backup file '{expected.RelativePath}' failed SHA-256 verification.");
            }
        }
    }

    private static async Task<InventoryBackupManifest> ReadManifestAsync(
        string backupDirectory,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(
            backupDirectory,
            ManifestFileName);
        await using var stream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<InventoryBackupManifest>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The backup manifest could not be read.");
    }

    private RestoreRehearsalReport CreateReport(
        string rehearsalId,
        string backupId,
        DateTimeOffset startedAtUtc,
        long durationMilliseconds,
        string status,
        IReadOnlyList<RestoreCheckEvidence> checks,
        string? failure) =>
        new(
            FormatVersion: 1,
            RehearsalId: rehearsalId,
            BackupId: backupId,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: _clock.UtcNow,
            DurationMilliseconds: durationMilliseconds,
            Status: status,
            Checks: checks,
            RollbackProcedure: RollbackProcedure,
            Failure: failure);

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(
            stream,
            value,
            JsonOptions,
            cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static string SafeCombine(string root, string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidOperationException(
                "Backup evidence contains an absolute path.");
        }

        var normalizedRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(
            Path.Combine(normalizedRoot, relativePath));
        var relative = Path.GetRelativePath(normalizedRoot, candidate);
        if (relative == ".." ||
            relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Backup evidence contains a path outside the backup directory.");
        }

        return candidate;
    }

    private static void DeleteOperationDirectory(
        string operationDirectory,
        string backupRoot)
    {
        if (!Directory.Exists(operationDirectory))
        {
            return;
        }

        var relative = Path.GetRelativePath(
            Path.GetFullPath(backupRoot),
            Path.GetFullPath(operationDirectory));
        if (relative == ".." ||
            relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to clean an operation directory outside the backup root.");
        }

        Directory.Delete(operationDirectory, recursive: true);
    }

    private static string GetRelease()
    {
        var assembly = typeof(BackupRestoreService).Assembly;
        return assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    [LoggerMessage(
        EventId = 1300,
        Level = LogLevel.Information,
        Message = "Backup {BackupId} completed in {DurationMilliseconds} ms with {FileCount} protected files")]
    private static partial void LogBackupCompleted(
        ILogger logger,
        string backupId,
        long durationMilliseconds,
        int fileCount);

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Information,
        Message = "Restore rehearsal {RehearsalId} for backup {BackupId} passed in {DurationMilliseconds} ms")]
    private static partial void LogRehearsalCompleted(
        ILogger logger,
        string rehearsalId,
        string backupId,
        long durationMilliseconds);

    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Error,
        Message = "Restore rehearsal {RehearsalId} for backup {BackupId} failed")]
    private static partial void LogRehearsalFailed(
        ILogger logger,
        string rehearsalId,
        string backupId,
        Exception exception);
}
