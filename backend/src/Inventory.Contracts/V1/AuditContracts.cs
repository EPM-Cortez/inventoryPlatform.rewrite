namespace Inventory.Contracts.V1;

public sealed record AuditLogResponse(
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

public sealed record AuditFilterVocabularyResponse(
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> TargetTypes,
    IReadOnlyList<string> Outcomes,
    bool Truncated);
