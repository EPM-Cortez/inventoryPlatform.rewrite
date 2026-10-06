using Inventory.Application.Abstractions;
using Inventory.Application.Attachments;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class EfAttachmentRepository : IAttachmentRepository
{
    private readonly InventoryDbContext _dbContext;

    public EfAttachmentRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<AssetAttachment?> GetAsync(
        Guid assetId,
        Guid attachmentId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<AssetAttachment>()
            .SingleOrDefaultAsync(
                item => item.AssetId == assetId && item.Id == attachmentId,
                cancellationToken);

    public async Task<AssetAttachmentView?> GetViewAsync(
        Guid assetId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var row = await ProjectRows(
                BuildQuery(assetId)
                    .Where(item => item.Id == attachmentId))
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : MapView(row);
    }

    public async Task<PageResult<AssetAttachmentView>> ListAsync(
        Guid assetId,
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = BuildQuery(assetId);
        if (!includeArchived)
        {
            query = query.Where(item => item.ArchivedAtUtc == null);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await ProjectRows(
                query
                    .OrderBy(item => item.ArchivedAtUtc != null)
                    .ThenByDescending(item => item.CreatedAtUtc)
                    .ThenBy(item => item.FileName)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize))
            .ToArrayAsync(cancellationToken);
        var items = rows.Select(MapView).ToArray();
        return new PageResult<AssetAttachmentView>(
            items,
            page,
            pageSize,
            totalCount);
    }

    public async Task<AssetAttachmentStorageUsage> GetStorageUsageAsync(
        Guid assetId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Set<AssetAttachment>()
            .AsNoTracking()
            .Where(item => item.AssetId == assetId);
        return new AssetAttachmentStorageUsage(
            await query.LongCountAsync(cancellationToken),
            await query.SumAsync(
                item => (long?)item.SizeBytes,
                cancellationToken) ?? 0);
    }

    public void Add(AssetAttachment attachment) =>
        _dbContext.Set<AssetAttachment>().Add(attachment);

    private IQueryable<AssetAttachment> BuildQuery(Guid assetId) =>
        _dbContext.Set<AssetAttachment>()
            .AsNoTracking()
            .Where(item => item.AssetId == assetId);

    private static IQueryable<AssetAttachmentViewRow> ProjectRows(
        IQueryable<AssetAttachment> query) =>
        query.Select(item => new AssetAttachmentViewRow(
            item.Id,
            item.AssetId,
            item.Category,
            item.FileName,
            item.ContentType,
            item.SizeBytes,
            item.Sha256,
            item.CreatedAtUtc,
            item.CreatedByUserId,
            item.CreatedByDisplayName,
            item.ArchivedAtUtc,
            item.ArchivedByUserId,
            item.ArchivedByDisplayName,
            item.Version));

    private static AssetAttachmentView MapView(AssetAttachmentViewRow item) =>
        new(
            item.Id,
            item.AssetId,
            (AttachmentCategoryView)item.Category,
            item.FileName,
            item.ContentType,
            item.SizeBytes,
            item.Sha256,
            item.CreatedAtUtc,
            item.CreatedByUserId,
            item.CreatedByDisplayName,
            item.ArchivedAtUtc,
            item.ArchivedByUserId,
            item.ArchivedByDisplayName,
            item.Version);

    private sealed record AssetAttachmentViewRow(
        Guid Id,
        Guid AssetId,
        AssetAttachmentCategory Category,
        string FileName,
        string ContentType,
        long SizeBytes,
        string Sha256,
        DateTimeOffset CreatedAtUtc,
        Guid CreatedByUserId,
        string CreatedByDisplayName,
        DateTimeOffset? ArchivedAtUtc,
        Guid? ArchivedByUserId,
        string? ArchivedByDisplayName,
        Guid Version);
}
