using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Application.SavedSearches;
using Inventory.Domain.Assets;
using Inventory.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Infrastructure.Data;

public sealed class EfSavedSearchRepository : ISavedSearchRepository
{
    private readonly InventoryDbContext _dbContext;

    public EfSavedSearchRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ISavedSearchTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        return new EfSavedSearchTransaction(transaction);
    }

    public Task<SavedAssetSearch?> GetAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) =>
        _dbContext.Set<SavedAssetSearch>()
            .SingleOrDefaultAsync(
                item =>
                    item.UserId == userId &&
                    item.Id == id &&
                    item.ArchivedAtUtc == null,
                cancellationToken);

    public Task<SavedAssetSearch?> GetDefaultAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<SavedAssetSearch>()
            .SingleOrDefaultAsync(
                item =>
                    item.UserId == userId &&
                    item.ArchivedAtUtc == null &&
                    item.IsDefault,
                cancellationToken);

    public Task<SavedAssetSearchRecord?> GetRecordAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken) =>
        ProjectRecords(
                BuildActiveQuery(userId)
                    .Where(item => item.Id == id))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<SavedAssetSearchRecord?> GetDefaultRecordAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        ProjectRecords(
                BuildActiveQuery(userId)
                    .Where(item => item.IsDefault))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PageResult<SavedAssetSearchRecord>> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = BuildActiveQuery(userId);
        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await ProjectRecords(
                query
                    .OrderByDescending(item => item.IsDefault)
                    .ThenBy(item => item.Name)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize))
            .ToArrayAsync(cancellationToken);
        return new PageResult<SavedAssetSearchRecord>(
            items,
            page,
            pageSize,
            totalCount);
    }

    public Task<int> CountActiveAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<SavedAssetSearch>()
            .CountAsync(
                item => item.UserId == userId && item.ArchivedAtUtc == null,
                cancellationToken);

    public Task<bool> NameExistsAsync(
        Guid userId,
        string normalizedName,
        Guid? excludingId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<SavedAssetSearch>()
            .AnyAsync(
                item =>
                    item.UserId == userId &&
                    item.ArchivedAtUtc == null &&
                    item.NameNormalized == normalizedName &&
                    (!excludingId.HasValue || item.Id != excludingId.Value),
                cancellationToken);

    public void Add(SavedAssetSearch savedSearch) =>
        _dbContext.Set<SavedAssetSearch>().Add(savedSearch);

    public void AddAuditLog(AuditLog auditLog) =>
        _dbContext.AuditLogs.Add(auditLog);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesWithConflictMappingAsync(
                "saved_search_conflict",
                "The saved search conflicts with existing data.",
                "The saved search was changed by another request.",
                cancellationToken);
        }
        catch (PersistenceConflictException exception)
            when (exception.GetBaseException().Message.Contains(
                "saved_search_limit_reached",
                StringComparison.Ordinal))
        {
            throw new PersistenceConflictException(
                "saved_search_limit_reached",
                "A user can keep at most 50 saved searches.",
                exception);
        }
    }

    private IQueryable<SavedAssetSearch> BuildActiveQuery(Guid userId) =>
        _dbContext.Set<SavedAssetSearch>()
            .AsNoTracking()
            .Where(
                item =>
                    item.UserId == userId &&
                    item.ArchivedAtUtc == null);

    private static IQueryable<SavedAssetSearchRecord> ProjectRecords(
        IQueryable<SavedAssetSearch> query) =>
        query.Select(item => new SavedAssetSearchRecord(
                item.Id,
                item.Name,
                item.SchemaVersion,
                item.FilterJson,
                item.IsDefault,
                item.CreatedAtUtc,
                item.UpdatedAtUtc,
                item.Version));

    private sealed class EfSavedSearchTransaction : ISavedSearchTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfSavedSearchTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync(CancellationToken cancellationToken) =>
            _transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken) =>
            _transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }
}
