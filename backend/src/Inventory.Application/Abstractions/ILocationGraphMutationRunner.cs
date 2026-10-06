namespace Inventory.Application.Abstractions;

/// <summary>
/// Runs a complete location-graph validation and mutation in one serializable
/// database transaction. The callback must perform its reads and save before it
/// returns; callers must not use an earlier graph snapshot to make decisions.
/// </summary>
public interface ILocationGraphMutationRunner
{
    Task<TResult> RunAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);
}
