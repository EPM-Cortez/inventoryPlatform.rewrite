using Inventory.Application.Common;
using Inventory.Domain.Warranties;

namespace Inventory.Application.Abstractions;

public interface IWarrantyRepository
{
    Task<WarrantyCoverage?> GetAsync(
        Guid warrantyId,
        CancellationToken cancellationToken);

    Task<WarrantyCoverage?> GetPrimaryAsync(
        Guid assetId,
        Guid? excludingWarrantyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WarrantyCoverage>> ListForAssetAsync(
        Guid assetId,
        bool includeArchived,
        CancellationToken cancellationToken);

    Task<PageResult<WarrantyCoverage>> ListExpiringAsync(
        DateOnly startsOnOrBefore,
        DateOnly endsOnOrBefore,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    void Add(WarrantyCoverage warranty);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
