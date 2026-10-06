using Inventory.Domain.Common;

namespace Inventory.Domain.School;

public sealed class SchoolProfile
{
    private SchoolProfile()
    {
    }

    private SchoolProfile(
        Guid id,
        string name,
        string code,
        string timeZone,
        Uri qrBaseUrl,
        int warrantyWarningDays)
    {
        Id = id;
        Update(name, code, timeZone, qrBaseUrl, warrantyWarningDays, expectedVersion: null);
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public string TimeZone { get; private set; } = "Europe/London";

    public string QrBaseUrl { get; private set; } = string.Empty;

    public int WarrantyWarningDays { get; private set; }

    public Guid Version { get; private set; }

    public static SchoolProfile Create(
        string name,
        string code,
        string timeZone,
        Uri qrBaseUrl,
        int warrantyWarningDays = 60) =>
        new(Guid.NewGuid(), name, code, timeZone, qrBaseUrl, warrantyWarningDays);

    public void Update(
        string name,
        string code,
        string timeZone,
        Uri qrBaseUrl,
        int warrantyWarningDays,
        Guid? expectedVersion)
    {
        if (expectedVersion.HasValue &&
            (expectedVersion.Value == Guid.Empty || expectedVersion.Value != Version))
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The school profile was changed by another user.");
        }

        if (!qrBaseUrl.IsAbsoluteUri || qrBaseUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new DomainRuleException(
                "invalid_qr_base_url",
                "The QR base URL must be an absolute HTTPS URL.");
        }

        if (warrantyWarningDays is < 1 or > 365)
        {
            throw new DomainRuleException(
                "invalid_warranty_warning_days",
                "Warranty warning days must be between 1 and 365.");
        }

        Name = TextRules.Required(name, nameof(Name), 200);
        Code = TextRules.Required(code, nameof(Code), 50);
        TimeZone = TextRules.Required(timeZone, nameof(TimeZone), 100);
        QrBaseUrl = qrBaseUrl.ToString().TrimEnd('/');
        WarrantyWarningDays = warrantyWarningDays;
        Version = Guid.NewGuid();
    }
}
