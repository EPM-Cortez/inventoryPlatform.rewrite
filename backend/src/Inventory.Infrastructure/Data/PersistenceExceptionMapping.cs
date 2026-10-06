using Inventory.Application.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inventory.Infrastructure.Data;

/// <summary>
/// Maps EF Core save failures onto <see cref="PersistenceConflictException"/> only when
/// they represent genuine data conflicts. Concurrency violations and SQLite constraint
/// and PostgreSQL constraint violations become conflicts; every other database
/// failure (disk full, busy/locked
/// database, coding faults) is rethrown unchanged so it surfaces as an unhandled server
/// error and is logged with its root cause instead of masquerading as a 409.
/// </summary>
internal static class PersistenceExceptionMapping
{
    private const int SqliteConstraintPrimaryResultCode = 19;

    public static async Task SaveChangesWithConflictMappingAsync(
        this DbContext dbContext,
        string conflictCode,
        string conflictMessage,
        string concurrencyMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PersistenceConflictException(
                "concurrency_conflict",
                concurrencyMessage,
                exception);
        }
        catch (DbUpdateException exception) when (IsConstraintViolation(exception))
        {
            throw new PersistenceConflictException(conflictCode, conflictMessage, exception);
        }
    }

    public static bool IsConstraintViolation(DbUpdateException exception) =>
        exception.GetBaseException() is SqliteException
        {
            SqliteErrorCode: SqliteConstraintPrimaryResultCode,
        } or PostgresException
        {
            SqlState: "23502" or "23503" or "23505" or "23514" or "23P01",
        };
}
