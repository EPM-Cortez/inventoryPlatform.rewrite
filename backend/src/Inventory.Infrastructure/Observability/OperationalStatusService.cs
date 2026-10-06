using Inventory.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Observability;

public sealed record OperationalAlert(
    string Severity,
    string Code,
    string Message);

public sealed record StorageStatus(
    bool Configured,
    long? AvailableFreeSpaceBytes,
    bool DatabaseConfigured,
    long? DatabaseAvailableFreeSpaceBytes,
    DateTimeOffset? LatestBackupAtUtc,
    double? LatestBackupAgeHours);

public sealed record OperationalStatus(
    OperationalMetricsSnapshot Metrics,
    StorageStatus Storage,
    IReadOnlyList<OperationalAlert> Alerts);

public sealed class OperationalStatusService
{
    private readonly IOperationalMetrics _metrics;
    private readonly StorageOptions _storageOptions;
    private readonly ObservabilityOptions _observabilityOptions;
    private readonly DatabaseStorageLocation _databaseStorageLocation;
    private readonly IStorageVolumeProbe _storageVolumeProbe;

    public OperationalStatusService(
        IOperationalMetrics metrics,
        IOptions<StorageOptions> storageOptions,
        IOptions<ObservabilityOptions> observabilityOptions,
        DatabaseStorageLocation databaseStorageLocation,
        IStorageVolumeProbe storageVolumeProbe)
    {
        _metrics = metrics;
        _storageOptions = storageOptions.Value;
        _observabilityOptions = observabilityOptions.Value;
        _databaseStorageLocation = databaseStorageLocation;
        _storageVolumeProbe = storageVolumeProbe;
    }

    public OperationalStatus GetStatus()
    {
        var metrics = _metrics.GetSnapshot();
        var storage = InspectStorage();
        var alerts = new List<OperationalAlert>();
        AddThresholdAlert(
            alerts,
            metrics.FailedLogins,
            _observabilityOptions.FailedLoginAlertThreshold,
            "failed_login_threshold",
            "Failed login attempts have reached the configured threshold.");
        AddThresholdAlert(
            alerts,
            metrics.FailedMfaChallenges,
            _observabilityOptions.FailedMfaAlertThreshold,
            "failed_mfa_threshold",
            "Failed MFA challenges have reached the configured threshold.");
        AddThresholdAlert(
            alerts,
            metrics.ImportRejections,
            _observabilityOptions.ImportRejectionAlertThreshold,
            "import_rejection_threshold",
            "Import rejections have reached the configured threshold.");
        AddThresholdAlert(
            alerts,
            metrics.QrFailures,
            _observabilityOptions.QrFailureAlertThreshold,
            "qr_failure_threshold",
            "QR failures have reached the configured threshold.");
        AddThresholdAlert(
            alerts,
            metrics.SqliteContentionEvents,
            _observabilityOptions.SqliteContentionAlertThreshold,
            "sqlite_contention_threshold",
            "SQLite contention has reached the configured threshold.");

        if (storage.Configured &&
            storage.AvailableFreeSpaceBytes < _storageOptions.MinimumFreeSpaceBytes)
        {
            alerts.Add(
                new OperationalAlert(
                    "critical",
                    "storage_capacity_low",
                    "Available storage is below the configured minimum."));
        }

        if (storage.DatabaseConfigured &&
            storage.DatabaseAvailableFreeSpaceBytes <
                _storageOptions.MinimumDatabaseFreeSpaceBytes)
        {
            alerts.Add(
                new OperationalAlert(
                    "critical",
                    "database_storage_capacity_low",
                    "Available SQLite database storage is below the configured minimum."));
        }

        if (storage.Configured && storage.LatestBackupAtUtc is null)
        {
            alerts.Add(
                new OperationalAlert(
                    "warning",
                    "backup_missing",
                    "No completed backup manifest was found."));
        }
        else if (storage.LatestBackupAgeHours > _storageOptions.MaximumBackupAgeHours)
        {
            alerts.Add(
                new OperationalAlert(
                    "critical",
                    "backup_stale",
                    "The latest completed backup is older than the configured maximum age."));
        }

        return new OperationalStatus(metrics, storage, alerts);
    }

    private StorageStatus InspectStorage()
    {
        var databasePath = _databaseStorageLocation.DatabasePath;
        var databaseConfigured = !string.IsNullOrWhiteSpace(databasePath);
        var databaseAvailableSpace = databaseConfigured
            ? InspectAvailableSpace(databasePath!)
            : (long?)null;

        if (string.IsNullOrWhiteSpace(_storageOptions.BackupPath))
        {
            return new StorageStatus(
                Configured: false,
                AvailableFreeSpaceBytes: null,
                DatabaseConfigured: databaseConfigured,
                DatabaseAvailableFreeSpaceBytes: databaseAvailableSpace,
                LatestBackupAtUtc: null,
                LatestBackupAgeHours: null);
        }

        var backupPath = Path.GetFullPath(_storageOptions.BackupPath);
        if (!Directory.Exists(backupPath))
        {
            return new StorageStatus(
                Configured: true,
                AvailableFreeSpaceBytes: 0,
                DatabaseConfigured: databaseConfigured,
                DatabaseAvailableFreeSpaceBytes: databaseAvailableSpace,
                LatestBackupAtUtc: null,
                LatestBackupAgeHours: null);
        }

        var availableSpace = InspectAvailableSpace(backupPath);

        var latestManifest = Directory
            .EnumerateDirectories(backupPath, "backup-*", SearchOption.TopDirectoryOnly)
            .Take(1_000)
            .Select(directory => Path.Combine(directory, "manifest.json"))
            .Where(File.Exists)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        var latestBackupAt = latestManifest is null
            ? (DateTimeOffset?)null
            : new DateTimeOffset(latestManifest.LastWriteTimeUtc, TimeSpan.Zero);
        var age = latestBackupAt.HasValue
            ? (DateTimeOffset.UtcNow - latestBackupAt.Value).TotalHours
            : (double?)null;
        return new StorageStatus(
            Configured: true,
            availableSpace,
            DatabaseConfigured: databaseConfigured,
            DatabaseAvailableFreeSpaceBytes: databaseAvailableSpace,
            latestBackupAt,
            age);
    }

    private long InspectAvailableSpace(string path)
    {
        try
        {
            return _storageVolumeProbe.GetAvailableFreeSpaceBytes(path);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException)
        {
            return 0;
        }
    }

    private static void AddThresholdAlert(
        List<OperationalAlert> alerts,
        long value,
        int threshold,
        string code,
        string message)
    {
        if (value >= threshold)
        {
            alerts.Add(new OperationalAlert("warning", code, message));
        }
    }
}
