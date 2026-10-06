using Inventory.Application.Abstractions;

namespace Inventory.Infrastructure.Data;

public sealed class ImportPreviewReservation
{
    private ImportPreviewReservation()
    {
    }

    public ImportPreviewReservation(
        Guid payloadId,
        ImportPreviewPayloadKind kind,
        Guid createdByUserId,
        int payloadBytes,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        PayloadId = payloadId;
        Kind = kind;
        CreatedByUserId = createdByUserId;
        PayloadBytes = payloadBytes;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid PayloadId { get; private set; }

    public ImportPreviewPayloadKind Kind { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public int PayloadBytes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? PurgedAtUtc { get; private set; }

    public DateTimeOffset? PreservedAtUtc { get; private set; }

    public void MarkPurged(DateTimeOffset now) => PurgedAtUtc = now;

    public void Preserve(DateTimeOffset now) => PreservedAtUtc = now;
}

public sealed class ImportPreviewBudgetGate
{
    public int Id { get; set; } = 1;

    public long Revision { get; set; }
}
