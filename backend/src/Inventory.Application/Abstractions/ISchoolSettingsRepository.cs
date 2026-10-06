using Inventory.Domain.Auditing;
using Inventory.Domain.School;

namespace Inventory.Application.Abstractions;

public interface ISchoolSettingsRepository
{
    Task<SchoolProfile?> GetAsync(
        bool forUpdate,
        CancellationToken cancellationToken);

    void Add(SchoolProfile profile);

    void AddAuditLog(AuditLog auditLog);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
