namespace Inventory.Application.School;

public sealed record SchoolSettingsView(
    string Name,
    string Code,
    string TimeZone,
    string QrBaseUrl,
    int WarrantyWarningDays,
    bool IsPersisted,
    Guid? Version);

public sealed record UpdateSchoolSettingsCommand(
    string Name,
    string Code,
    string TimeZone,
    string QrBaseUrl,
    int WarrantyWarningDays,
    Guid? Version);
