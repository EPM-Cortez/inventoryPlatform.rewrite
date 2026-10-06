using Inventory.Domain.Agents;
using Inventory.Application.Assets;

namespace Inventory.Application.Agents;

public sealed record AgentIdentity(
    string Hostname,
    string? SerialNumber,
    string? BiosUuid,
    string? Manufacturer,
    string? Model);

public sealed record AgentHardwareSnapshot(
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? BiosUuid,
    string? BiosVersion,
    string? Processor,
    int? ProcessorCores,
    int? LogicalProcessors,
    long? MemoryBytes);

public sealed record AgentOperatingSystemSnapshot(
    string? Name,
    string? Version,
    string? Build,
    string? Architecture,
    DateTimeOffset? InstalledAtUtc,
    DateTimeOffset? LastBootAtUtc);

public sealed record AgentSecuritySnapshot(
    bool? SecureBootEnabled,
    bool? TpmPresent,
    bool? TpmEnabled,
    string? TpmVersion,
    bool? SystemDriveProtected,
    string? AntivirusProducts);

public sealed record AgentNetworkAdapterSnapshot(
    string Name,
    string? Type,
    string? MacAddress,
    IReadOnlyList<string> Ipv4Addresses);

public sealed record AgentStorageVolumeSnapshot(
    string Name,
    string? Label,
    string? FileSystem,
    long TotalBytes,
    long FreeBytes);

public sealed record AgentInstalledSoftwareSnapshot(
    string Name,
    string? Version,
    string? Publisher,
    string? Architecture,
    DateOnly? InstalledOn);

public sealed record AgentDeviceSnapshot(
    Guid SnapshotId,
    DateTimeOffset CollectedAtUtc,
    string AgentVersion,
    string Hostname,
    string? Domain,
    AgentHardwareSnapshot Hardware,
    AgentOperatingSystemSnapshot OperatingSystem,
    AgentSecuritySnapshot Security,
    IReadOnlyList<AgentNetworkAdapterSnapshot> NetworkAdapters,
    IReadOnlyList<AgentStorageVolumeSnapshot> StorageVolumes,
    IReadOnlyList<AgentInstalledSoftwareSnapshot> InstalledSoftware);

public sealed record EnrollAgentCommand(
    string EnrollmentToken,
    long TimestampEpochSeconds,
    string Nonce,
    string DeviceId,
    string PublicKeyPem,
    string SignatureAlgorithm,
    AgentKeyProtection KeyProtection,
    string KeyThumbprintSha256,
    AgentIdentity Identity,
    string Signature,
    string? IpAddress,
    string? Client,
    string? CorrelationId);

public sealed record AgentEnrollmentResult(
    Guid AgentId,
    string DeviceId,
    DateTimeOffset ServerTimeUtc,
    long NextSequence,
    int CheckInIntervalSeconds);

public sealed record CheckInAgentCommand(
    Guid AgentId,
    string DeviceId,
    long Sequence,
    long TimestampEpochSeconds,
    string Nonce,
    string BodySha256,
    string Signature,
    AgentDeviceSnapshot Snapshot,
    string? IpAddress,
    string? Client,
    string? CorrelationId);

public sealed record AgentCheckInResult(
    bool Accepted,
    bool Duplicate,
    Guid AgentId,
    Guid SnapshotId,
    DateTimeOffset ReceivedAtUtc,
    long NextSequence,
    Guid? LinkedAssetId,
    int CheckInIntervalSeconds);

public sealed record CreateAgentEnrollmentProfileCommand(
    string Name,
    DateTimeOffset ExpiresAtUtc,
    int MaxEnrollments,
    bool RequireTpm,
    string? AllowedHostnameSuffix);

public sealed record AgentEnrollmentProfileView(
    Guid Id,
    string Name,
    DateTimeOffset ExpiresAtUtc,
    int MaxEnrollments,
    int EnrolledCount,
    bool RequireTpm,
    string? AllowedHostnameSuffix,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? RevokedAtUtc,
    Guid Version);

public sealed record CreatedAgentEnrollmentProfile(
    AgentEnrollmentProfileView Profile,
    string EnrollmentToken);

public enum ManagedAgentConnectivityStatus
{
    Online,
    Stale,
    Offline,
    NeverCheckedIn,
}

public sealed record ManagedAgentSearch(
    string? Query,
    bool IncludeRevoked,
    bool? IsRevoked,
    ManagedAgentMatchState? MatchState,
    ManagedAgentConnectivityStatus? Connectivity,
    DateTimeOffset OnlineCutoffUtc,
    DateTimeOffset StaleCutoffUtc,
    int Page,
    int PageSize);

public sealed record ManagedAgentFleetSummary(
    long Total,
    long Active,
    long Revoked,
    long Matched,
    long Unmatched,
    long Conflict,
    long Dismissed,
    long Online,
    long Stale,
    long Offline,
    long NeverCheckedIn);

public sealed record AgentAssetMatchResult(
    Guid? AssetId,
    bool HasConflict,
    bool HasTerminalMatch = false);

public sealed record AgentAssetLinkTarget(
    Guid AssetId,
    bool IsTerminal);

public sealed record ManagedAgentListItem(
    Guid Id,
    Guid EnrollmentProfileId,
    string EnrollmentProfileName,
    string DeviceId,
    string Hostname,
    string? SerialNumber,
    string? BiosUuid,
    string? Manufacturer,
    string? Model,
    AgentKeyProtection KeyProtection,
    bool IsRevoked,
    ManagedAgentMatchState MatchState,
    string? MatchReason,
    Guid? LinkedAssetId,
    string? LinkedAssetTag,
    string? LinkedAssetDeviceName,
    DateTimeOffset EnrolledAtUtc,
    DateTimeOffset? LastCheckInAtUtc,
    DateTimeOffset? LastCollectedAtUtc,
    string? LastSeenIpAddress,
    string? LastAgentVersion,
    long LastSequence,
    DateTimeOffset? RevokedAtUtc,
    string? RevokedReason,
    DateTimeOffset? KeyReplacedAtUtc,
    int KeyReplacementCount,
    Guid Version,
    ManagedAgentConnectivityStatus ConnectivityStatus =
        ManagedAgentConnectivityStatus.NeverCheckedIn,
    DateTimeOffset? NextExpectedCheckInAtUtc = null,
    DateTimeOffset? OverdueAtUtc = null);

public sealed record ManagedAgentDetail(
    ManagedAgentListItem Agent,
    AgentDeviceSnapshot? LatestSnapshot);

public sealed record LinkManagedAgentCommand(
    Guid AgentId,
    Guid AssetId,
    string? Reason,
    Guid Version);

public sealed record UnlinkManagedAgentCommand(
    Guid AgentId,
    string Reason,
    Guid Version);

public sealed record DismissManagedAgentCommand(
    Guid AgentId,
    string Reason,
    Guid Version);

public sealed record RestoreManagedAgentReviewCommand(
    Guid AgentId,
    string Reason,
    Guid Version);

public sealed record RevokeManagedAgentCommand(
    Guid AgentId,
    string Reason,
    Guid Version);

public sealed record CreateAgentKeyRecoveryCommand(
    Guid AgentId,
    string Reason,
    DateTimeOffset ExpiresAtUtc,
    bool RequireTpm,
    Guid Version);

public sealed record CreatedAgentKeyRecovery(
    Guid AuthorizationId,
    Guid AgentId,
    DateTimeOffset ExpiresAtUtc,
    bool RequireTpm,
    string RecoveryToken,
    Guid Version);

public sealed record RevokeAgentKeyRecoveryCommand(
    Guid AgentId,
    Guid AuthorizationId,
    string Reason,
    Guid Version);

public enum AgentKeyRecoveryAuthorizationStatus
{
    Pending = 1,
    Expired = 2,
    Consumed = 3,
    Revoked = 4,
}

public sealed record AgentKeyRecoveryAuthorizationView(
    Guid AuthorizationId,
    Guid AgentId,
    AgentKeyRecoveryAuthorizationStatus Status,
    string Reason,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    string CreatedByDisplayName,
    DateTimeOffset ExpiresAtUtc,
    bool RequireTpm,
    DateTimeOffset? ConsumedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    Guid? RevokedByUserId,
    string? RevokedReason,
    Guid Version);

public sealed record CreateAndLinkManagedAgentAssetCommand(
    Guid AgentId,
    CreateAssetCommand Asset,
    string Reason,
    Guid Version);

public sealed record CreateAndLinkManagedAgentAssetResult(
    AssetView Asset,
    ManagedAgentDetail Agent);

public sealed class AgentSecuritySettings
{
    public int CheckInIntervalSeconds { get; init; } = 21_600;

    public int ConnectivityGraceSeconds { get; init; } = 900;

    public int ConnectivityOfflineAfterSeconds { get; init; } = 259_200;

    public int MaximumEnrollmentProfileDays { get; init; } = 30;

    public int MaximumOfflineSnapshotDays { get; init; } = 30;

    public int MaximumKeyRecoveryMinutes { get; init; } = 60;

    public int CheckInBurstWindowSeconds { get; init; } = 900;

    public int MaximumCheckInsPerAgentBurst { get; init; } = 144;

    public int CheckInQuotaWindowHours { get; init; } = 24;

    public int MaximumCheckInsPerAgentQuotaWindow { get; init; } = 192;

    public int MaximumSnapshotRecordsPerAgent { get; init; } = 64;

    public long MaximumSnapshotPayloadBytesPerAgent { get; init; } =
        16L * 1024 * 1024;

    public void Validate()
    {
        if (CheckInIntervalSeconds is < 300 or > 86_400)
        {
            throw new InvalidOperationException(
                "Agents:CheckInIntervalSeconds must be between 300 and 86400.");
        }

        if (ConnectivityGraceSeconds is < 60 or > 86_400)
        {
            throw new InvalidOperationException(
                "Agents:ConnectivityGraceSeconds must be between 60 and 86400.");
        }

        if (ConnectivityOfflineAfterSeconds <=
                CheckInIntervalSeconds + ConnectivityGraceSeconds ||
            ConnectivityOfflineAfterSeconds > 2_592_000)
        {
            throw new InvalidOperationException(
                "Agents:ConnectivityOfflineAfterSeconds must be greater than " +
                "the check-in interval plus grace and no more than 2592000.");
        }

        if (MaximumEnrollmentProfileDays is < 1 or > 90)
        {
            throw new InvalidOperationException(
                "Agents:MaximumEnrollmentProfileDays must be between 1 and 90.");
        }

        if (MaximumOfflineSnapshotDays is < 1 or > 90)
        {
            throw new InvalidOperationException(
                "Agents:MaximumOfflineSnapshotDays must be between 1 and 90.");
        }

        if (MaximumKeyRecoveryMinutes is < 5 or > 1440)
        {
            throw new InvalidOperationException(
                "Agents:MaximumKeyRecoveryMinutes must be between 5 and 1440.");
        }

        if (CheckInBurstWindowSeconds is < 60 or > 3600)
        {
            throw new InvalidOperationException(
                "Agents:CheckInBurstWindowSeconds must be between 60 and 3600.");
        }

        if (MaximumCheckInsPerAgentBurst is < 129 or > 1000)
        {
            throw new InvalidOperationException(
                "Agents:MaximumCheckInsPerAgentBurst must be between 129 and 1000.");
        }

        if (CheckInQuotaWindowHours is < 1 or > 24)
        {
            throw new InvalidOperationException(
                "Agents:CheckInQuotaWindowHours must be between 1 and 24.");
        }

        if (MaximumCheckInsPerAgentQuotaWindow <
                MaximumCheckInsPerAgentBurst ||
            MaximumCheckInsPerAgentQuotaWindow > 5000)
        {
            throw new InvalidOperationException(
                "Agents:MaximumCheckInsPerAgentQuotaWindow must be at least " +
                "MaximumCheckInsPerAgentBurst and no more than 5000.");
        }

        if (MaximumSnapshotRecordsPerAgent is < 8 or > 1000)
        {
            throw new InvalidOperationException(
                "Agents:MaximumSnapshotRecordsPerAgent must be between 8 and 1000.");
        }

        if (MaximumSnapshotPayloadBytesPerAgent is <
                2L * 1024 * 1024 or
            > 1024L * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "Agents:MaximumSnapshotPayloadBytesPerAgent must be between " +
                "2097152 and 1073741824.");
        }
    }
}
