namespace Inventory.Application.Abstractions;

public sealed record SchoolSettingsSnapshot(
    string Name,
    string Code,
    string TimeZone,
    string QrBaseUrl,
    int WarrantyWarningDays);

public interface ISchoolSettingsProvider
{
    Task<SchoolSettingsSnapshot> GetAsync(CancellationToken cancellationToken);
}
