namespace Inventory.Contracts.V1;

public sealed record OperationalMetricsResponse(
    DateTimeOffset StartedAtUtc,
    long UptimeSeconds,
    long FailedLogins,
    long FailedMfaChallenges,
    long ImportRejections,
    long QrFailures,
    long SqliteContentionEvents,
    long ReadinessFailures);

public sealed record OperationalStorageResponse(
    bool Configured,
    long? AvailableFreeSpaceBytes,
    bool DatabaseConfigured,
    long? DatabaseAvailableFreeSpaceBytes,
    DateTimeOffset? LatestBackupAtUtc,
    double? LatestBackupAgeHours);

public sealed record OperationalAlertResponse(
    string Severity,
    string Code,
    string Message);

public sealed record OperationalStatusResponse(
    OperationalMetricsResponse Metrics,
    OperationalStorageResponse Storage,
    IReadOnlyList<OperationalAlertResponse> Alerts);
