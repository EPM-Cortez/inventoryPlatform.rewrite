namespace Inventory.Infrastructure.Configuration;

public sealed class SchoolFeatureOptions
{
    public string Name { get; init; } = "School";

    public string Code { get; init; } = "SCHOOL";

    public string TimeZone { get; init; } = "Europe/London";

    public string QrBaseUrl { get; init; } = string.Empty;

    public int WarrantyWarningDays { get; init; } = 60;
}
