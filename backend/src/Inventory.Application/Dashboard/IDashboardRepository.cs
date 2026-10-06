using Inventory.Application.Common;

namespace Inventory.Application.Dashboard;

public interface IDashboardRepository
{
    Task<DashboardSummaryView> GetSummaryAsync(
        int categoryLimit,
        DateOnly today,
        CancellationToken cancellationToken);

    Task<InventoryDistributionView> GetInventoryDistributionAsync(
        InventoryDistributionSearch search,
        CancellationToken cancellationToken);

    Task<PageResult<DashboardExceptionAssetView>> GetExceptionQueueAsync(
        string exceptionCode,
        DateOnly today,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
