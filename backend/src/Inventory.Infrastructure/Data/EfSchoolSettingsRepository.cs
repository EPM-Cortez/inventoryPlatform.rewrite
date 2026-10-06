using Inventory.Application.Abstractions;
using Inventory.Domain.Auditing;
using Inventory.Domain.School;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class EfSchoolSettingsRepository : ISchoolSettingsRepository
{
    private readonly InventoryDbContext _dbContext;

    public EfSchoolSettingsRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SchoolProfile?> GetAsync(
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var query = forUpdate
            ? _dbContext.SchoolProfiles.AsQueryable()
            : _dbContext.SchoolProfiles.AsNoTracking();
        var profiles = await query
            .OrderBy(profile => profile.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        return profiles.Length switch
        {
            0 => null,
            1 => profiles[0],
            _ => throw new InvalidOperationException(
                "SchoolProfile must contain at most one row."),
        };
    }

    public void Add(SchoolProfile profile) =>
        _dbContext.SchoolProfiles.Add(profile);

    public void AddAuditLog(AuditLog auditLog) =>
        _dbContext.AuditLogs.Add(auditLog);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesWithConflictMappingAsync(
            "school_settings_conflict",
            "The school settings conflict with another change.",
            "The school settings were changed by another user.",
            cancellationToken);
}
