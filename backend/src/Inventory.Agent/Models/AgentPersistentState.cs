using Inventory.Contracts.V1;

namespace Inventory.Agent.Models;

public sealed class AgentPersistentState
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Guid? AgentId { get; set; }

    public string? DeviceId { get; set; }

    public string? KeyThumbprintSha256 { get; set; }

    public AgentKeyProtectionContract? KeyProtection { get; set; }

    public DateTimeOffset? EnrolledAtUtc { get; set; }

    public int CheckInIntervalSeconds { get; set; } = 21_600;

    public long NextSequence { get; set; } = 1;

    public List<PendingAgentSnapshot> PendingSnapshots { get; set; } = [];

    public List<AgentTerminalFailure> TerminalFailures { get; set; } = [];

    public bool IsEnrolled =>
        AgentId.HasValue &&
        AgentId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(DeviceId) &&
        !string.IsNullOrWhiteSpace(KeyThumbprintSha256) &&
        KeyProtection.HasValue;

    public bool Upgrade(int fallbackCheckInIntervalSeconds)
    {
        if (SchemaVersion != 1)
        {
            return false;
        }

        if (fallbackCheckInIntervalSeconds is < 300 or > 86_400)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fallbackCheckInIntervalSeconds));
        }

        SchemaVersion = CurrentSchemaVersion;
        CheckInIntervalSeconds = fallbackCheckInIntervalSeconds;
        return true;
    }
}

public sealed class PendingAgentSnapshot
{
    public Guid QueueId { get; set; } = Guid.NewGuid();

    public long Sequence { get; set; }

    public AgentDeviceSnapshotRequest Snapshot { get; set; } = new();

    public DateTimeOffset QueuedAtUtc { get; set; }

    public DateTimeOffset NextAttemptAtUtc { get; set; }

    public int AttemptCount { get; set; }
}

public sealed class AgentTerminalFailure
{
    public Guid SnapshotId { get; set; }

    public long Sequence { get; set; }

    public DateTimeOffset FailedAtUtc { get; set; }

    public string ProblemType { get; set; } = string.Empty;
}
