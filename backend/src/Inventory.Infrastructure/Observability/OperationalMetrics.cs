using System.Diagnostics.Metrics;

namespace Inventory.Infrastructure.Observability;

public sealed record OperationalMetricsSnapshot(
    DateTimeOffset StartedAtUtc,
    long FailedLogins,
    long FailedMfaChallenges,
    long ImportRejections,
    long QrFailures,
    long SqliteContentionEvents,
    long ReadinessFailures);

public interface IOperationalMetrics
{
    void RecordFailedLogin();

    void RecordFailedMfaChallenge();

    void RecordImportRejection();

    void RecordQrFailure();

    void RecordSqliteContention();

    void RecordReadinessFailure();

    OperationalMetricsSnapshot GetSnapshot();
}

public sealed class OperationalMetrics : IOperationalMetrics
{
    private static readonly Meter Meter = new("Inventory.Rewrite", "1.0.0");
    private static readonly Counter<long> FailedLoginCounter =
        Meter.CreateCounter<long>("inventory.auth.login.failures");
    private static readonly Counter<long> FailedMfaCounter =
        Meter.CreateCounter<long>("inventory.auth.mfa.failures");
    private static readonly Counter<long> ImportRejectionCounter =
        Meter.CreateCounter<long>("inventory.import.rejections");
    private static readonly Counter<long> QrFailureCounter =
        Meter.CreateCounter<long>("inventory.qr.failures");
    private static readonly Counter<long> SqliteContentionCounter =
        Meter.CreateCounter<long>("inventory.sqlite.contention");
    private static readonly Counter<long> ReadinessFailureCounter =
        Meter.CreateCounter<long>("inventory.readiness.failures");

    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private long _failedLogins;
    private long _failedMfaChallenges;
    private long _importRejections;
    private long _qrFailures;
    private long _sqliteContentionEvents;
    private long _readinessFailures;

    public void RecordFailedLogin()
    {
        Interlocked.Increment(ref _failedLogins);
        FailedLoginCounter.Add(1);
    }

    public void RecordFailedMfaChallenge()
    {
        Interlocked.Increment(ref _failedMfaChallenges);
        FailedMfaCounter.Add(1);
    }

    public void RecordImportRejection()
    {
        Interlocked.Increment(ref _importRejections);
        ImportRejectionCounter.Add(1);
    }

    public void RecordQrFailure()
    {
        Interlocked.Increment(ref _qrFailures);
        QrFailureCounter.Add(1);
    }

    public void RecordSqliteContention()
    {
        Interlocked.Increment(ref _sqliteContentionEvents);
        SqliteContentionCounter.Add(1);
    }

    public void RecordReadinessFailure()
    {
        Interlocked.Increment(ref _readinessFailures);
        ReadinessFailureCounter.Add(1);
    }

    public OperationalMetricsSnapshot GetSnapshot() =>
        new(
            _startedAtUtc,
            Interlocked.Read(ref _failedLogins),
            Interlocked.Read(ref _failedMfaChallenges),
            Interlocked.Read(ref _importRejections),
            Interlocked.Read(ref _qrFailures),
            Interlocked.Read(ref _sqliteContentionEvents),
            Interlocked.Read(ref _readinessFailures));
}
