using Inventory.Application.Common;
using Inventory.Application.SavedSearches;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;

namespace Inventory.Application.Abstractions;

public interface ISavedSearchRepository
{
    Task<ISavedSearchTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken);

    Task<SavedAssetSearch?> GetAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken);

    Task<SavedAssetSearch?> GetDefaultAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<SavedAssetSearchRecord?> GetRecordAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken);

    Task<SavedAssetSearchRecord?> GetDefaultRecordAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<PageResult<SavedAssetSearchRecord>> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<int> CountActiveAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(
        Guid userId,
        string normalizedName,
        Guid? excludingId,
        CancellationToken cancellationToken);

    void Add(SavedAssetSearch savedSearch);

    void AddAuditLog(AuditLog auditLog);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ISavedSearchTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);
}
