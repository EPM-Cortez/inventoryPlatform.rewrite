namespace Inventory.Infrastructure.Configuration;

public sealed class ImportPreviewOptions
{
    public const string SectionName = "ImportPreviews";

    public int RetentionDays { get; set; } = 7;

    public int MaximumActiveJobsPerUser { get; set; } = 10;

    public long MaximumBytesPerUser { get; set; } = 64L * 1024 * 1024;

    public long MaximumBytesOverall { get; set; } = 512L * 1024 * 1024;

    public void Validate()
    {
        if (RetentionDays is < 1 or > 30 ||
            MaximumActiveJobsPerUser is < 1 or > 100 ||
            MaximumBytesPerUser < 1 ||
            MaximumBytesOverall < MaximumBytesPerUser ||
            MaximumBytesOverall > 16L * 1024 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "Import preview retention or storage limits are invalid.");
        }
    }
}
