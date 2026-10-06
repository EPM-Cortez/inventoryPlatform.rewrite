namespace Inventory.Contracts.V1;

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount);

public sealed record VersionRequest(Guid Version);
