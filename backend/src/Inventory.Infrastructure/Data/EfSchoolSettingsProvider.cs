using Inventory.Application.Abstractions;
using Inventory.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Data;

public sealed class EfSchoolSettingsProvider : ISchoolSettingsProvider
{
    private readonly InventoryDbContext _dbContext;
    private readonly SchoolFeatureOptions _fallback;

    public EfSchoolSettingsProvider(
        InventoryDbContext dbContext,
        IOptions<SchoolFeatureOptions> fallback)
    {
        _dbContext = dbContext;
        _fallback = fallback.Value;
    }

    public async Task<SchoolSettingsSnapshot> GetAsync(
        CancellationToken cancellationToken)
    {
        var profiles = await _dbContext.SchoolProfiles
            .AsNoTracking()
            .OrderBy(profile => profile.Id)
            .Select(profile => new SchoolSettingsSnapshot(
                profile.Name,
                profile.Code,
                profile.TimeZone,
                profile.QrBaseUrl,
                profile.WarrantyWarningDays))
            .Take(2)
            .ToArrayAsync(cancellationToken);

        return profiles.Length switch
        {
            0 => new SchoolSettingsSnapshot(
                _fallback.Name.Trim(),
                _fallback.Code.Trim(),
                _fallback.TimeZone.Trim(),
                _fallback.QrBaseUrl.TrimEnd('/'),
                _fallback.WarrantyWarningDays),
            1 => profiles[0],
            _ => throw new InvalidOperationException(
                "SchoolProfile must contain at most one row."),
        };
    }
}
