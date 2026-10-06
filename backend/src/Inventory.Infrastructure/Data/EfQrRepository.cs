using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Assets;
using Inventory.Domain.Qr;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class EfQrRepository : IQrRepository
{
    private readonly InventoryDbContext _dbContext;

    public EfQrRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<QrIdentifier?> GetActiveAsync(
        Guid assetId,
        CancellationToken cancellationToken) =>
        _dbContext.QrIdentifiers.SingleOrDefaultAsync(
            identifier =>
                identifier.AssetId == assetId &&
                identifier.RevokedAtUtc == null,
            cancellationToken);

    public Task<QrIdentifier?> GetActiveByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken) =>
        _dbContext.QrIdentifiers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                identifier =>
                    identifier.TokenHash == tokenHash &&
                    identifier.RevokedAtUtc == null,
                cancellationToken);

    public async Task<int> GetLatestTokenVersionAsync(
        Guid assetId,
        CancellationToken cancellationToken) =>
        await _dbContext.QrIdentifiers
            .Where(identifier => identifier.AssetId == assetId)
            .Select(identifier => (int?)identifier.TokenVersion)
            .MaxAsync(cancellationToken)
        ?? 0;

    public void Add(QrIdentifier identifier) =>
        _dbContext.QrIdentifiers.Add(identifier);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        SaveWithConflictMappingAsync(cancellationToken);

    public async Task SaveRotationAsync(
        QrIdentifier replacement,
        AssetEvent rotationEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await SaveWithConflictMappingAsync(cancellationToken);
            _dbContext.QrIdentifiers.Add(replacement);
            _dbContext.AssetEvents.Add(rotationEvent);
            await SaveWithConflictMappingAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private Task SaveWithConflictMappingAsync(
        CancellationToken cancellationToken) =>
        _dbContext.SaveChangesWithConflictMappingAsync(
            "qr_conflict",
            "The QR identifier change conflicts with existing data.",
            "The QR identifier was changed by another user.",
            cancellationToken);
}
