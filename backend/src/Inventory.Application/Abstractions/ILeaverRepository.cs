using Inventory.Application.Common;
using Inventory.Application.Leavers;
using Inventory.Domain.Auditing;
using Inventory.Domain.People;

namespace Inventory.Application.Abstractions;

public interface ILeaverRepository
{
    Task<LeaverCase?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> HasOpenCaseAsync(
        Guid personId,
        Guid? excludingCaseId,
        CancellationToken cancellationToken);

    Task<LeaverCaseDetailView?> GetViewAsync(
        Guid id,
        DateOnly schoolToday,
        CancellationToken cancellationToken);

    Task<PageResult<LeaverCustodyAssetView>> GetCurrentAssetsAsync(
        Guid personId,
        DateOnly schoolToday,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<PageResult<LeaverCaseSummaryView>> SearchAsync(
        LeaverCaseSearch search,
        DateOnly schoolToday,
        CancellationToken cancellationToken);

    Task<PageResult<CustodyExceptionView>> SearchExceptionsAsync(
        DateOnly schoolToday,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    void Add(LeaverCase leaverCase);

    void AddAuditLog(AuditLog auditLog);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
