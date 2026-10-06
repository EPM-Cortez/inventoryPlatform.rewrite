using Inventory.Domain.Common;

namespace Inventory.Domain.Auditing;

public sealed class AuditLog
{
    private AuditLog()
    {
    }

    private AuditLog(
        Guid id,
        Guid? actorUserId,
        string? actorDisplayName,
        string action,
        string targetType,
        string? targetId,
        DateTimeOffset occurredAtUtc,
        string outcome,
        string? detailsJson,
        string? ipAddress,
        string? client,
        string? correlationId)
    {
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException("utc_required", "Audit timestamps must be UTC.");
        }

        Id = id;
        ActorUserId = actorUserId;
        ActorDisplayName = TextRules.Optional(actorDisplayName, nameof(ActorDisplayName), 200);
        Action = AuditVocabulary.CanonicalizeAction(
            TextRules.Required(action, nameof(Action), 100));
        TargetType = AuditVocabulary.CanonicalizeTargetType(
            TextRules.Required(targetType, nameof(TargetType), 100));
        TargetId = TextRules.Optional(targetId, nameof(TargetId), 200);
        OccurredAtUtc = occurredAtUtc;
        Outcome = AuditVocabulary.CanonicalizeOutcome(
            TextRules.Required(outcome, nameof(Outcome), 50));
        DetailsJson = TextRules.Optional(detailsJson, nameof(DetailsJson), 16000);
        IpAddress = TextRules.Optional(ipAddress, nameof(IpAddress), 100);
        Client = TextRules.Optional(client, nameof(Client), 500);
        CorrelationId = TextRules.Optional(correlationId, nameof(CorrelationId), 100);
    }

    public Guid Id { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? ActorDisplayName { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string TargetType { get; private set; } = string.Empty;

    public string? TargetId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string Outcome { get; private set; } = string.Empty;

    public string? DetailsJson { get; private set; }

    public string? IpAddress { get; private set; }

    public string? Client { get; private set; }

    public string? CorrelationId { get; private set; }

    public static AuditLog Create(
        Guid? actorUserId,
        string? actorDisplayName,
        string action,
        string targetType,
        string? targetId,
        DateTimeOffset occurredAtUtc,
        string outcome,
        string? detailsJson = null,
        string? ipAddress = null,
        string? client = null,
        string? correlationId = null) =>
        new(
            Guid.NewGuid(),
            actorUserId,
            actorDisplayName,
            action,
            targetType,
            targetId,
            occurredAtUtc,
            outcome,
            detailsJson,
            ipAddress,
            client,
            correlationId);
}
