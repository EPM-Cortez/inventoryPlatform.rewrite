using System.Data;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inventory.Infrastructure.Data;

public sealed class EfLocationGraphMutationRunner : ILocationGraphMutationRunner
{
    private readonly InventoryDbContext _dbContext;

    public EfLocationGraphMutationRunner(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TResult> RunAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (IsSerializationOrLockConflict(exception))
        {
            throw new PersistenceConflictException(
                "location_graph_conflict",
                "The location hierarchy changed concurrently. Retry the operation with current data.",
                exception);
        }
    }

    private static bool IsSerializationOrLockConflict(Exception exception) =>
        exception.GetBaseException() is PostgresException
        {
            SqlState: PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected,
        } or SqliteException
        {
            SqliteErrorCode: 5 or 6,
        };
}
