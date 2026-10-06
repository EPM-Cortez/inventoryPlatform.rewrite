using Inventory.Domain.Common;

namespace Inventory.Domain.Assets;

public sealed class AssetEvent
{
    private AssetEvent()
    {
    }

    private AssetEvent(
        Guid id,
        Guid assetId,
        AssetEventType type,
        DateTimeOffset occurredAtUtc,
        Guid actorUserId,
        string actorDisplayName,
        AssetEventSource source,
        string summary,
        string detailsJson,
        string? correlationId)
    {
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                "Asset event timestamps must be UTC.");
        }

        Id = id;
        AssetId = assetId;
        Type = type;
        OccurredAtUtc = occurredAtUtc;
        ActorUserId = actorUserId;
        ActorDisplayName = TextRules.Required(actorDisplayName, nameof(ActorDisplayName), 200);
        Source = source;
        Summary = TextRules.Required(summary, nameof(Summary), 500);
        DetailsJson = TextRules.Required(detailsJson, nameof(DetailsJson), 16000);
        CorrelationId = TextRules.Optional(correlationId, nameof(CorrelationId), 100);
    }

    public Guid Id { get; private set; }

    public Guid AssetId { get; private set; }

    public AssetEventType Type { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public Guid ActorUserId { get; private set; }

    public string ActorDisplayName { get; private set; } = string.Empty;

    public AssetEventSource Source { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    public string DetailsJson { get; private set; } = "{}";

    public string? CorrelationId { get; private set; }

    public static AssetEvent Create(
        Guid assetId,
        AssetEventType type,
        DateTimeOffset occurredAtUtc,
        Guid actorUserId,
        string actorDisplayName,
        AssetEventSource source,
        string summary,
        string detailsJson,
        string? correlationId = null) =>
        new(
            Guid.NewGuid(),
            assetId,
            type,
            occurredAtUtc,
            actorUserId,
            actorDisplayName,
            source,
            summary,
            detailsJson,
            correlationId);
}
