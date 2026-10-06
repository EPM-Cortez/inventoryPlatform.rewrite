namespace Inventory.Infrastructure.Operations;

public sealed class PostgresExternalBackupRestoreService : IBackupRestoreService
{
    private const string Message =
        "PostgreSQL backups and restore rehearsals must use pg_dump and pg_restore " +
        "outside the API process. See docs/docker-postgres.md.";

    public Task<BackupOperationResult> CreateBackupAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<BackupOperationResult>(
            new NotSupportedException(Message));
    }

    public Task<RestoreRehearsalResult> RehearseLatestRestoreAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<RestoreRehearsalResult>(
            new NotSupportedException(Message));
    }
}
