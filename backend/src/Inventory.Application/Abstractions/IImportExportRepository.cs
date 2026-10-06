using Inventory.Application.DataExchange;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Auditing;
using Inventory.Domain.Importing;
using Inventory.Domain.Warranties;

namespace Inventory.Application.Abstractions;

public interface IImportExportRepository
{
    Task<AssetImportJob?> GetImportJobAsync(
        Guid importJobId,
        bool forUpdate,
        CancellationToken cancellationToken);

    Task<ImportLookupSnapshot> GetImportLookupsAsync(
        ImportLookupRequest request,
        CancellationToken cancellationToken);

    Task<ImportCommitState> GetImportCommitStateAsync(
        ImportCommitLookupRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AssetExportRow>> ExportAssetsAsync(
        AssetExportFilter filter,
        int take,
        CancellationToken cancellationToken);

    void AddImportJob(AssetImportJob job);

    void AddCategory(AssetCategory category);

    void AddLocation(Inventory.Domain.Locations.Location location);

    void AddAsset(Asset asset);

    void AddAssignment(AssetAssignment assignment);

    void AddWarranty(WarrantyCoverage warranty);

    void AddAssetEvent(AssetEvent assetEvent);

    void AddAuditLog(AuditLog auditLog);

    Task SaveRejectedCommitAuditAfterFailureAsync(
        AuditLog auditLog,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
