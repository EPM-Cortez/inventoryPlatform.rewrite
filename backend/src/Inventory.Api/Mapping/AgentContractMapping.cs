using Inventory.Application.Agents;
using Inventory.Contracts.V1;
using Inventory.Domain.Agents;

namespace Inventory.Api.Mapping;

internal static class AgentContractMapping
{
    public static ManagedAgentMatchState ToDomain(
        this ManagedAgentMatchStateContract state) =>
        state switch
        {
            ManagedAgentMatchStateContract.Unmatched =>
                ManagedAgentMatchState.Unmatched,
            ManagedAgentMatchStateContract.Matched =>
                ManagedAgentMatchState.Matched,
            ManagedAgentMatchStateContract.Conflict =>
                ManagedAgentMatchState.Conflict,
            ManagedAgentMatchStateContract.Dismissed =>
                ManagedAgentMatchState.Dismissed,
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                null),
        };

    public static ManagedAgentConnectivityStatus ToDomain(
        this ManagedAgentConnectivityStatusContract status) =>
        status switch
        {
            ManagedAgentConnectivityStatusContract.Online =>
                ManagedAgentConnectivityStatus.Online,
            ManagedAgentConnectivityStatusContract.Stale =>
                ManagedAgentConnectivityStatus.Stale,
            ManagedAgentConnectivityStatusContract.Offline =>
                ManagedAgentConnectivityStatus.Offline,
            ManagedAgentConnectivityStatusContract.NeverCheckedIn =>
                ManagedAgentConnectivityStatus.NeverCheckedIn,
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                null),
        };

    public static AgentIdentity ToApplication(
        this AgentIdentityRequest identity) =>
        new(
            identity.Hostname,
            identity.SerialNumber,
            identity.BiosUuid,
            identity.Manufacturer,
            identity.Model);

    public static AgentDeviceSnapshot ToApplication(
        this AgentDeviceSnapshotRequest snapshot) =>
        new(
            snapshot.SnapshotId,
            snapshot.CollectedAtUtc,
            snapshot.AgentVersion,
            snapshot.Hostname,
            snapshot.Domain,
            new AgentHardwareSnapshot(
                snapshot.Hardware.Manufacturer,
                snapshot.Hardware.Model,
                snapshot.Hardware.SerialNumber,
                snapshot.Hardware.BiosUuid,
                snapshot.Hardware.BiosVersion,
                snapshot.Hardware.Processor,
                snapshot.Hardware.ProcessorCores,
                snapshot.Hardware.LogicalProcessors,
                snapshot.Hardware.MemoryBytes),
            new AgentOperatingSystemSnapshot(
                snapshot.OperatingSystem.Name,
                snapshot.OperatingSystem.Version,
                snapshot.OperatingSystem.Build,
                snapshot.OperatingSystem.Architecture,
                snapshot.OperatingSystem.InstalledAtUtc,
                snapshot.OperatingSystem.LastBootAtUtc),
            new AgentSecuritySnapshot(
                snapshot.Security.SecureBootEnabled,
                snapshot.Security.TpmPresent,
                snapshot.Security.TpmEnabled,
                snapshot.Security.TpmVersion,
                snapshot.Security.SystemDriveProtected,
                snapshot.Security.AntivirusProducts),
            snapshot.NetworkAdapters
                .Select(
                    adapter => new AgentNetworkAdapterSnapshot(
                        adapter.Name,
                        adapter.Type,
                        adapter.MacAddress,
                        adapter.Ipv4Addresses))
                .ToArray(),
            snapshot.StorageVolumes
                .Select(
                    volume => new AgentStorageVolumeSnapshot(
                        volume.Name,
                        volume.Label,
                        volume.FileSystem,
                        volume.TotalBytes,
                        volume.FreeBytes))
                .ToArray(),
            snapshot.InstalledSoftware
                .Select(
                    software => new AgentInstalledSoftwareSnapshot(
                        software.Name,
                        software.Version,
                        software.Publisher,
                        software.Architecture,
                        software.InstalledOn))
                .ToArray());

    public static AgentDeviceSnapshotRequest ToContract(
        this AgentDeviceSnapshot snapshot) =>
        new()
        {
            SnapshotId = snapshot.SnapshotId,
            CollectedAtUtc = snapshot.CollectedAtUtc,
            AgentVersion = snapshot.AgentVersion,
            Hostname = snapshot.Hostname,
            Domain = snapshot.Domain,
            Hardware = new AgentHardwareSnapshotRequest
            {
                Manufacturer = snapshot.Hardware.Manufacturer,
                Model = snapshot.Hardware.Model,
                SerialNumber = snapshot.Hardware.SerialNumber,
                BiosUuid = snapshot.Hardware.BiosUuid,
                BiosVersion = snapshot.Hardware.BiosVersion,
                Processor = snapshot.Hardware.Processor,
                ProcessorCores = snapshot.Hardware.ProcessorCores,
                LogicalProcessors = snapshot.Hardware.LogicalProcessors,
                MemoryBytes = snapshot.Hardware.MemoryBytes,
            },
            OperatingSystem = new AgentOperatingSystemSnapshotRequest
            {
                Name = snapshot.OperatingSystem.Name,
                Version = snapshot.OperatingSystem.Version,
                Build = snapshot.OperatingSystem.Build,
                Architecture = snapshot.OperatingSystem.Architecture,
                InstalledAtUtc = snapshot.OperatingSystem.InstalledAtUtc,
                LastBootAtUtc = snapshot.OperatingSystem.LastBootAtUtc,
            },
            Security = new AgentSecuritySnapshotRequest
            {
                SecureBootEnabled = snapshot.Security.SecureBootEnabled,
                TpmPresent = snapshot.Security.TpmPresent,
                TpmEnabled = snapshot.Security.TpmEnabled,
                TpmVersion = snapshot.Security.TpmVersion,
                SystemDriveProtected =
                    snapshot.Security.SystemDriveProtected,
                AntivirusProducts = snapshot.Security.AntivirusProducts,
            },
            NetworkAdapters = snapshot.NetworkAdapters
                .Select(
                    adapter => new AgentNetworkAdapterSnapshotRequest
                    {
                        Name = adapter.Name,
                        Type = adapter.Type,
                        MacAddress = adapter.MacAddress,
                        Ipv4Addresses = adapter.Ipv4Addresses,
                    })
                .ToArray(),
            StorageVolumes = snapshot.StorageVolumes
                .Select(
                    volume => new AgentStorageVolumeSnapshotRequest
                    {
                        Name = volume.Name,
                        Label = volume.Label,
                        FileSystem = volume.FileSystem,
                        TotalBytes = volume.TotalBytes,
                        FreeBytes = volume.FreeBytes,
                    })
                .ToArray(),
            InstalledSoftware = snapshot.InstalledSoftware
                .Select(
                    software => new AgentInstalledSoftwareSnapshotRequest
                    {
                        Name = software.Name,
                        Version = software.Version,
                        Publisher = software.Publisher,
                        Architecture = software.Architecture,
                        InstalledOn = software.InstalledOn,
                    })
                .ToArray(),
        };

    public static AgentKeyProtection ToDomain(
        this AgentKeyProtectionContract keyProtection) =>
        keyProtection switch
        {
            AgentKeyProtectionContract.Tpm => AgentKeyProtection.Tpm,
            AgentKeyProtectionContract.Software => AgentKeyProtection.Software,
            _ => throw new ArgumentOutOfRangeException(
                nameof(keyProtection),
                keyProtection,
                "Unsupported endpoint key protection."),
        };

    public static AgentEnrollmentProfileResponse ToContract(
        this AgentEnrollmentProfileView profile) =>
        new(
            profile.Id,
            profile.Name,
            profile.ExpiresAtUtc,
            profile.MaxEnrollments,
            profile.EnrolledCount,
            profile.RequireTpm,
            profile.AllowedHostnameSuffix,
            profile.CreatedAtUtc,
            profile.CreatedByUserId,
            profile.RevokedAtUtc,
            profile.Version);

    public static ManagedAgentListItemResponse ToContract(
        this ManagedAgentListItem agent) =>
        new(
            agent.Id,
            agent.EnrollmentProfileId,
            agent.EnrollmentProfileName,
            agent.DeviceId,
            agent.Hostname,
            agent.SerialNumber,
            agent.BiosUuid,
            agent.Manufacturer,
            agent.Model,
            agent.KeyProtection == AgentKeyProtection.Tpm
                ? AgentKeyProtectionContract.Tpm
                : AgentKeyProtectionContract.Software,
            agent.IsRevoked
                ? ManagedAgentStatusContract.Revoked
                : ManagedAgentStatusContract.Active,
            agent.MatchState switch
            {
                ManagedAgentMatchState.Unmatched =>
                    ManagedAgentMatchStateContract.Unmatched,
                ManagedAgentMatchState.Matched =>
                    ManagedAgentMatchStateContract.Matched,
                ManagedAgentMatchState.Conflict =>
                    ManagedAgentMatchStateContract.Conflict,
                ManagedAgentMatchState.Dismissed =>
                    ManagedAgentMatchStateContract.Dismissed,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(agent),
                    agent.MatchState,
                    null),
            },
            agent.MatchReason,
            agent.ConnectivityStatus switch
            {
                ManagedAgentConnectivityStatus.Online =>
                    ManagedAgentConnectivityStatusContract.Online,
                ManagedAgentConnectivityStatus.Stale =>
                    ManagedAgentConnectivityStatusContract.Stale,
                ManagedAgentConnectivityStatus.Offline =>
                    ManagedAgentConnectivityStatusContract.Offline,
                ManagedAgentConnectivityStatus.NeverCheckedIn =>
                    ManagedAgentConnectivityStatusContract.NeverCheckedIn,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(agent),
                    agent.ConnectivityStatus,
                    null),
            },
            agent.LinkedAssetId,
            agent.LinkedAssetTag,
            agent.LinkedAssetDeviceName,
            agent.EnrolledAtUtc,
            agent.LastCheckInAtUtc,
            agent.LastCollectedAtUtc,
            agent.LastSeenIpAddress,
            agent.NextExpectedCheckInAtUtc,
            agent.OverdueAtUtc,
            agent.LastAgentVersion,
            agent.LastSequence,
            agent.RevokedAtUtc,
            agent.RevokedReason,
            agent.KeyReplacedAtUtc,
            agent.KeyReplacementCount,
            agent.Version,
            HardwareAttestationVerified: false);

    public static ManagedAgentDetailResponse ToContract(
        this ManagedAgentDetail detail) =>
        new(
            detail.Agent.ToContract(),
            detail.LatestSnapshot?.ToContract());

    public static ManagedAgentFleetSummaryResponse ToContract(
        this ManagedAgentFleetSummary summary) =>
        new(
            summary.Total,
            summary.Active,
            summary.Revoked,
            summary.Matched,
            summary.Unmatched,
            summary.Conflict,
            summary.Dismissed,
            summary.Online,
            summary.Stale,
            summary.Offline,
            summary.NeverCheckedIn);
}
