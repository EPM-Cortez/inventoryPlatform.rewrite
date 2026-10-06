namespace Inventory.Infrastructure.Configuration;

public enum AttachmentScannerProvider
{
    WindowsDefender,
    ClamAv,
}

public sealed class AttachmentScanningOptions
{
    public bool Enabled { get; init; } = true;

    public AttachmentScannerProvider Provider { get; init; } =
        AttachmentScannerProvider.WindowsDefender;

    public string ExecutablePath { get; init; } = string.Empty;

    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 3310;

    public int TimeoutSeconds { get; init; } = 60;

    public int MaximumSignatureAgeHours { get; init; } = 72;

    public void Validate()
    {
        if (TimeoutSeconds is < 5 or > 300)
        {
            throw new InvalidOperationException(
                "AttachmentScanning:TimeoutSeconds must be between 5 and 300.");
        }

        if (MaximumSignatureAgeHours is < 1 or > 168)
        {
            throw new InvalidOperationException(
                "AttachmentScanning:MaximumSignatureAgeHours must be between 1 and 168.");
        }

        if (Provider == AttachmentScannerProvider.ClamAv)
        {
            if (string.IsNullOrWhiteSpace(Host) || Host.Length > 253)
            {
                throw new InvalidOperationException(
                    "AttachmentScanning:Host must identify the ClamAV service.");
            }

            if (Port is < 1 or > 65535)
            {
                throw new InvalidOperationException(
                    "AttachmentScanning:Port must be between 1 and 65535.");
            }

            if (!string.IsNullOrWhiteSpace(ExecutablePath))
            {
                throw new InvalidOperationException(
                    "AttachmentScanning:ExecutablePath is only valid for Windows Defender.");
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(ExecutablePath) &&
            !Path.IsPathFullyQualified(ExecutablePath))
        {
            throw new InvalidOperationException(
                "AttachmentScanning:ExecutablePath must be an absolute path.");
        }

        if (!string.IsNullOrWhiteSpace(ExecutablePath) &&
            !File.Exists(Path.GetFullPath(ExecutablePath)))
        {
            throw new InvalidOperationException(
                "AttachmentScanning:ExecutablePath must identify an existing scanner executable.");
        }
    }
}

public sealed class StorageOptions
{
    public string BackupPath { get; init; } = string.Empty;

    public string RehearsalLoginEmail { get; init; } = string.Empty;

    public string RehearsalLoginPassword { get; init; } = string.Empty;

    public bool RehearsalRequiresActiveQr { get; init; } = true;

    public long MinimumFreeSpaceBytes { get; init; } = 1L * 1024 * 1024 * 1024;

    public long MinimumDatabaseFreeSpaceBytes { get; init; } =
        1L * 1024 * 1024 * 1024;

    public int MaximumBackupAgeHours { get; init; } = 24;

    public void Validate()
    {
        if (MinimumFreeSpaceBytes is < 64L * 1024 * 1024 or > 10L * 1024 * 1024 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "Storage:MinimumFreeSpaceBytes must be between 64 MB and 10 TB.");
        }

        if (MinimumDatabaseFreeSpaceBytes is < 64L * 1024 * 1024 or
            > 10L * 1024 * 1024 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "Storage:MinimumDatabaseFreeSpaceBytes must be between 64 MB and 10 TB.");
        }

        if (MaximumBackupAgeHours is < 1 or > 24 * 31)
        {
            throw new InvalidOperationException(
                "Storage:MaximumBackupAgeHours must be between 1 and 744.");
        }
    }
}

public sealed class ObservabilityOptions
{
    public int FailedLoginAlertThreshold { get; init; } = 20;

    public int FailedMfaAlertThreshold { get; init; } = 10;

    public int ImportRejectionAlertThreshold { get; init; } = 10;

    public int QrFailureAlertThreshold { get; init; } = 10;

    public int SqliteContentionAlertThreshold { get; init; } = 1;

    public void Validate()
    {
        if (FailedLoginAlertThreshold < 1 ||
            FailedMfaAlertThreshold < 1 ||
            ImportRejectionAlertThreshold < 1 ||
            QrFailureAlertThreshold < 1 ||
            SqliteContentionAlertThreshold < 1)
        {
            throw new InvalidOperationException(
                "Observability alert thresholds must be positive.");
        }
    }
}
