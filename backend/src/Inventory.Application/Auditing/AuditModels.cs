namespace Inventory.Application.Auditing;

public sealed record AuditLogSearch(
    string? Action,
    string? TargetType,
    string? TargetId,
    Guid? ActorUserId,
    string? Outcome,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize);

public sealed record AuditLogView(
    Guid Id,
    Guid? ActorUserId,
    string? ActorDisplayName,
    string Action,
    string TargetType,
    string? TargetId,
    DateTimeOffset OccurredAtUtc,
    string Outcome,
    string? DetailsJson,
    string? IpAddress,
    string? Client,
    string? CorrelationId);

public sealed record AuditExportFile(
    byte[] Content,
    string FileName,
    int RowCount,
    bool Truncated);

public sealed record AuditFilterVocabulary(
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> TargetTypes,
    IReadOnlyList<string> Outcomes,
    bool Truncated);

public sealed class AuditExportSettings
{
    public int MaxExportRows { get; init; } = 10_000;

    public void Validate()
    {
        if (MaxExportRows is < 1 or > 50_000)
        {
            throw new InvalidOperationException(
                "Audit:MaxExportRows must be between 1 and 50000.");
        }
    }
}
