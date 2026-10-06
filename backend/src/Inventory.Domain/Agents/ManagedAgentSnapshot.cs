using Inventory.Domain.Common;

namespace Inventory.Domain.Agents;

public sealed class ManagedAgentSnapshot
{
    private ManagedAgentSnapshot()
    {
    }

    private ManagedAgentSnapshot(
        Guid agentId,
        Guid snapshotId,
        long sequence,
        DateTimeOffset collectedAtUtc,
        DateTimeOffset receivedAtUtc,
        string bodySha256,
        string contentSha256,
        string? payloadJson,
        string hostname,
        string? serialNumber,
        string? biosUuid,
        string agentVersion)
    {
        ValidateUtc(collectedAtUtc, nameof(collectedAtUtc));
        ValidateUtc(receivedAtUtc, nameof(receivedAtUtc));
        if (agentId == Guid.Empty || snapshotId == Guid.Empty || sequence < 1)
        {
            throw new DomainRuleException(
                "agent_snapshot_identity_invalid",
                "Agent, snapshot and sequence identifiers are required.");
        }

        Id = Guid.NewGuid();
        AgentId = agentId;
        SnapshotId = snapshotId;
        Sequence = sequence;
        CollectedAtUtc = collectedAtUtc;
        ReceivedAtUtc = receivedAtUtc;
        BodySha256 = TextRules.Required(bodySha256, nameof(BodySha256), 44);
        ContentSha256 = TextRules.Required(
            contentSha256,
            nameof(ContentSha256),
            44);
        PayloadJson = TextRules.Optional(
            payloadJson,
            nameof(PayloadJson),
            1_048_576);
        Hostname = TextRules.Required(hostname, nameof(Hostname), 150);
        SerialNumber = TextRules.Optional(
            serialNumber,
            nameof(SerialNumber),
            200);
        BiosUuid = TextRules.Optional(biosUuid, nameof(BiosUuid), 100);
        AgentVersion = TextRules.Required(agentVersion, nameof(AgentVersion), 50);
    }

    public Guid Id { get; private set; }

    public Guid AgentId { get; private set; }

    public Guid SnapshotId { get; private set; }

    public long Sequence { get; private set; }

    public DateTimeOffset CollectedAtUtc { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public string BodySha256 { get; private set; } = string.Empty;

    public string ContentSha256 { get; private set; } = string.Empty;

    public string? PayloadJson { get; private set; }

    public string Hostname { get; private set; } = string.Empty;

    public string? SerialNumber { get; private set; }

    public string? BiosUuid { get; private set; }

    public string AgentVersion { get; private set; } = string.Empty;

    public static ManagedAgentSnapshot Create(
        Guid agentId,
        Guid snapshotId,
        long sequence,
        DateTimeOffset collectedAtUtc,
        DateTimeOffset receivedAtUtc,
        string bodySha256,
        string contentSha256,
        string? payloadJson,
        string hostname,
        string? serialNumber,
        string? biosUuid,
        string agentVersion) =>
        new(
            agentId,
            snapshotId,
            sequence,
            collectedAtUtc,
            receivedAtUtc,
            bodySha256,
            contentSha256,
            payloadJson,
            hostname,
            serialNumber,
            biosUuid,
            agentVersion);

    private static void ValidateUtc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                $"{name} must be UTC.");
        }
    }
}
