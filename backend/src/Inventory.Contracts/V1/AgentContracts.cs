using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public enum AgentKeyProtectionContract
{
    Tpm,
    Software,
}

public enum ManagedAgentStatusContract
{
    Active,
    Revoked,
}

public enum ManagedAgentMatchStateContract
{
    Unmatched,
    Matched,
    Conflict,
    Dismissed,
}

public enum ManagedAgentConnectivityStatusContract
{
    Online,
    Stale,
    Offline,
    NeverCheckedIn,
}

public sealed class AgentIdentityRequest
{
    [Required]
    [StringLength(150)]
    public string Hostname { get; init; } = string.Empty;

    [StringLength(200)]
    public string? SerialNumber { get; init; }

    [StringLength(100)]
    public string? BiosUuid { get; init; }

    [StringLength(150)]
    public string? Manufacturer { get; init; }

    [StringLength(150)]
    public string? Model { get; init; }
}

public sealed class AgentEnrollmentRequest
{
    [Required]
    [StringLength(512)]
    public string EnrollmentToken { get; init; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long TimestampEpochSeconds { get; init; }

    [Required]
    [StringLength(128)]
    public string Nonce { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string DeviceId { get; init; } = string.Empty;

    [Required]
    [StringLength(2000)]
    public string PublicKeyPem { get; init; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string SignatureAlgorithm { get; init; } = string.Empty;

    public AgentKeyProtectionContract KeyProtection { get; init; }

    [Required]
    [StringLength(64, MinimumLength = 64)]
    public string KeyThumbprintSha256 { get; init; } = string.Empty;

    [Required]
    public AgentIdentityRequest Identity { get; init; } = new();

    [Required]
    [StringLength(256)]
    public string Signature { get; init; } = string.Empty;
}

public sealed record AgentEnrollmentResponse(
    Guid AgentId,
    string DeviceId,
    DateTimeOffset ServerTimeUtc,
    long NextSequence,
    int CheckInIntervalSeconds);

public sealed class AgentHardwareSnapshotRequest
{
    [StringLength(150)]
    public string? Manufacturer { get; init; }

    [StringLength(150)]
    public string? Model { get; init; }

    [StringLength(200)]
    public string? SerialNumber { get; init; }

    [StringLength(100)]
    public string? BiosUuid { get; init; }

    [StringLength(200)]
    public string? BiosVersion { get; init; }

    [StringLength(200)]
    public string? Processor { get; init; }

    [Range(0, int.MaxValue)]
    public int? ProcessorCores { get; init; }

    [Range(0, int.MaxValue)]
    public int? LogicalProcessors { get; init; }

    [Range(0, long.MaxValue)]
    public long? MemoryBytes { get; init; }
}

public sealed class AgentOperatingSystemSnapshotRequest
{
    [StringLength(200)]
    public string? Name { get; init; }

    [StringLength(100)]
    public string? Version { get; init; }

    [StringLength(100)]
    public string? Build { get; init; }

    [StringLength(50)]
    public string? Architecture { get; init; }

    public DateTimeOffset? InstalledAtUtc { get; init; }

    public DateTimeOffset? LastBootAtUtc { get; init; }
}

public sealed class AgentSecuritySnapshotRequest
{
    public bool? SecureBootEnabled { get; init; }

    public bool? TpmPresent { get; init; }

    public bool? TpmEnabled { get; init; }

    [StringLength(100)]
    public string? TpmVersion { get; init; }

    public bool? SystemDriveProtected { get; init; }

    [StringLength(500)]
    public string? AntivirusProducts { get; init; }
}

public sealed class AgentNetworkAdapterSnapshotRequest
{
    [Required]
    [StringLength(150)]
    public string Name { get; init; } = string.Empty;

    [StringLength(50)]
    public string? Type { get; init; }

    [StringLength(17)]
    public string? MacAddress { get; init; }

    public IReadOnlyList<string> Ipv4Addresses { get; init; } = [];
}

public sealed class AgentStorageVolumeSnapshotRequest
{
    [Required]
    [StringLength(20)]
    public string Name { get; init; } = string.Empty;

    [StringLength(100)]
    public string? Label { get; init; }

    [StringLength(30)]
    public string? FileSystem { get; init; }

    [Range(0, long.MaxValue)]
    public long TotalBytes { get; init; }

    [Range(0, long.MaxValue)]
    public long FreeBytes { get; init; }
}

public sealed class AgentInstalledSoftwareSnapshotRequest
{
    [Required]
    [StringLength(300)]
    public string Name { get; init; } = string.Empty;

    [StringLength(100)]
    public string? Version { get; init; }

    [StringLength(200)]
    public string? Publisher { get; init; }

    [StringLength(50)]
    public string? Architecture { get; init; }

    public DateOnly? InstalledOn { get; init; }
}

public sealed class AgentDeviceSnapshotRequest
{
    public Guid SnapshotId { get; init; }

    public DateTimeOffset CollectedAtUtc { get; init; }

    [Required]
    [StringLength(50)]
    public string AgentVersion { get; init; } = string.Empty;

    [Required]
    [StringLength(150)]
    public string Hostname { get; init; } = string.Empty;

    [StringLength(255)]
    public string? Domain { get; init; }

    public AgentHardwareSnapshotRequest Hardware { get; init; } = new();

    public AgentOperatingSystemSnapshotRequest OperatingSystem { get; init; } = new();

    public AgentSecuritySnapshotRequest Security { get; init; } = new();

    public IReadOnlyList<AgentNetworkAdapterSnapshotRequest> NetworkAdapters { get; init; } = [];

    public IReadOnlyList<AgentStorageVolumeSnapshotRequest> StorageVolumes { get; init; } = [];

    public IReadOnlyList<AgentInstalledSoftwareSnapshotRequest> InstalledSoftware { get; init; } = [];
}

public sealed class AgentCheckInRequest
{
    public Guid AgentId { get; init; }

    [Required]
    [StringLength(100)]
    public string DeviceId { get; init; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long Sequence { get; init; }

    [Range(1, long.MaxValue)]
    public long TimestampEpochSeconds { get; init; }

    [Required]
    [StringLength(128)]
    public string Nonce { get; init; } = string.Empty;

    [Required]
    [StringLength(44, MinimumLength = 44)]
    public string BodySha256 { get; init; } = string.Empty;

    [Required]
    [StringLength(256)]
    public string Signature { get; init; } = string.Empty;

    [Required]
    public AgentDeviceSnapshotRequest Snapshot { get; init; } = new();
}

public sealed record AgentCheckInResponse(
    bool Accepted,
    bool Duplicate,
    Guid AgentId,
    Guid SnapshotId,
    DateTimeOffset ReceivedAtUtc,
    long NextSequence,
    Guid? LinkedAssetId,
    int CheckInIntervalSeconds);

public sealed class CreateAgentEnrollmentProfileRequest
{
    [Required]
    [StringLength(150)]
    public string Name { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; init; }

    [Range(1, 5000)]
    public int MaxEnrollments { get; init; } = 1;

    public bool RequireTpm { get; init; } = true;

    [StringLength(150)]
    public string? AllowedHostnameSuffix { get; init; }
}

public sealed record AgentEnrollmentProfileResponse(
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

public sealed record CreatedAgentEnrollmentProfileResponse(
    AgentEnrollmentProfileResponse Profile,
    string EnrollmentToken);

public sealed record ManagedAgentListItemResponse(
    Guid Id,
    Guid EnrollmentProfileId,
    string EnrollmentProfileName,
    string DeviceId,
    string Hostname,
    string? SerialNumber,
    string? BiosUuid,
    string? Manufacturer,
    string? Model,
    AgentKeyProtectionContract KeyProtection,
    ManagedAgentStatusContract Status,
    ManagedAgentMatchStateContract MatchState,
    string? MatchReason,
    ManagedAgentConnectivityStatusContract ConnectivityStatus,
    Guid? LinkedAssetId,
    string? LinkedAssetTag,
    string? LinkedAssetDeviceName,
    DateTimeOffset EnrolledAtUtc,
    DateTimeOffset? LastCheckInAtUtc,
    DateTimeOffset? LastCollectedAtUtc,
    string? LastSeenIpAddress,
    DateTimeOffset? NextExpectedCheckInAtUtc,
    DateTimeOffset? OverdueAtUtc,
    string? LastAgentVersion,
    long LastSequence,
    DateTimeOffset? RevokedAtUtc,
    string? RevokedReason,
    DateTimeOffset? KeyReplacedAtUtc,
    int KeyReplacementCount,
    Guid Version,
    bool HardwareAttestationVerified);

public sealed record ManagedAgentDetailResponse(
    ManagedAgentListItemResponse Agent,
    AgentDeviceSnapshotRequest? LatestSnapshot);

public sealed record ManagedAgentFleetSummaryResponse(
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

public sealed class LinkManagedAgentRequest
{
    public Guid AssetId { get; init; }

    [StringLength(500)]
    public string? Reason { get; init; }

    public Guid Version { get; init; }
}

public sealed class UnlinkManagedAgentRequest
{
    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    public Guid Version { get; init; }
}

public sealed class DismissManagedAgentRequest
{
    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    public Guid Version { get; init; }
}

public sealed class RestoreManagedAgentReviewRequest
{
    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    public Guid Version { get; init; }
}

public sealed class RevokeManagedAgentRequest
{
    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    public Guid Version { get; init; }
}

public sealed class CreateAgentKeyRecoveryRequest
{
    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public bool RequireTpm { get; init; } = true;

    public Guid Version { get; init; }
}

public sealed record CreatedAgentKeyRecoveryResponse(
    Guid AuthorizationId,
    Guid AgentId,
    DateTimeOffset ExpiresAtUtc,
    bool RequireTpm,
    string RecoveryToken,
    Guid Version);

public sealed class RevokeAgentKeyRecoveryRequest
{
    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    public Guid Version { get; init; }
}

public enum AgentKeyRecoveryAuthorizationStatusContract
{
    Pending = 1,
    Expired = 2,
    Consumed = 3,
    Revoked = 4,
}

public sealed record AgentKeyRecoveryAuthorizationResponse(
    Guid AuthorizationId,
    Guid AgentId,
    AgentKeyRecoveryAuthorizationStatusContract Status,
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

public sealed class CreateAndLinkManagedAgentAssetRequest
    : CreateAssetRequest
{
    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    public Guid Version { get; init; }
}

public sealed record CreateAndLinkManagedAgentAssetResponse(
    AssetResponse Asset,
    ManagedAgentDetailResponse Agent);

public sealed class RevokeAgentEnrollmentProfileRequest
{
    public Guid Version { get; init; }
}
