namespace Inventory.Application.Common;

public static class SchoolBusinessDate
{
    public static DateOnly FromUtc(
        DateTimeOffset utcNow,
        string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(utcNow, timeZone).DateTime);
    }
}
