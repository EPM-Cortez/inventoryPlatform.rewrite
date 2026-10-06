using Inventory.Application.Auditing;
using Inventory.Application.Common;
using Inventory.Domain.Auditing;

namespace Inventory.Application.Abstractions;

public interface IAuditLogRepository
{
    Task<PageResult<AuditLogView>> SearchAsync(
        AuditLogSearch search,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditLogView>> ExportAsync(
        AuditLogSearch search,
        int take,
        CancellationToken cancellationToken);

    Task<AuditFilterVocabulary> GetFilterVocabularyAsync(
        int maximumValuesPerField,
        CancellationToken cancellationToken);

    void Add(AuditLog auditLog);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
