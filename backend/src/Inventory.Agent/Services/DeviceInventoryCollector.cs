using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Inventory.Contracts.V1;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace Inventory.Agent.Services;

public sealed record CollectedAgentIdentity(
    string DeviceId,
    AgentIdentityRequest Identity);

public interface IDeviceInventoryCollector
{
    CollectedAgentIdentity CollectIdentity();

    AgentDeviceSnapshotRequest CollectSnapshot();
}

public sealed partial class DeviceInventoryCollector
    : IDeviceInventoryCollector
{
    private static readonly HashSet<string> PlaceholderValues =
        new(
            [
                "default string",
                "none",
                "not applicable",
                "not specified",
                "system serial number",
                "to be filled by o.e.m.",
                "unknown",
            ],
            StringComparer.OrdinalIgnoreCase);

    private readonly IOptionsMonitor<AgentOptions> _options;
    private readonly ILogger<DeviceInventoryCollector> _logger;

    public DeviceInventoryCollector(
        IOptionsMonitor<AgentOptions> options,
        ILogger<DeviceInventoryCollector> logger)
    {
        _options = options;
        _logger = logger;
    }

    public CollectedAgentIdentity CollectIdentity()
    {
        EnsureWindows();
        var hardware = CollectHardware();
        return new CollectedAgentIdentity(
            BuildDeviceId(),
            new AgentIdentityRequest
            {
                Hostname = GetHostname(),
                SerialNumber = hardware.SerialNumber,
                BiosUuid = hardware.BiosUuid,
                Manufacturer = hardware.Manufacturer,
                Model = hardware.Model,
            });
    }

    public AgentDeviceSnapshotRequest CollectSnapshot()
    {
        EnsureWindows();
        var hardware = CollectHardware();
        return new AgentDeviceSnapshotRequest
        {
            SnapshotId = Guid.NewGuid(),
            CollectedAtUtc = DateTimeOffset.UtcNow,
            AgentVersion = Limit(
                Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                ?? Assembly.GetExecutingAssembly()
                    .GetName()
                    .Version
                    ?.ToString()
                ?? "1.0.0",
                50)!,
            Hostname = GetHostname(),
            Domain = Limit(GetNetworkDomain(), 255),
            Hardware = hardware,
            OperatingSystem = CollectOperatingSystem(),
            Security = CollectSecurity(),
            NetworkAdapters = CollectNetworkAdapters(),
            StorageVolumes = CollectStorageVolumes(),
            InstalledSoftware = _options.CurrentValue.CollectInstalledSoftware
                ? CollectInstalledSoftware()
                : [],
        };
    }

    private AgentHardwareSnapshotRequest CollectHardware()
    {
        var computerSystem = ReadFirst(
            @"root\CIMV2",
            "SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem");
        var product = ReadFirst(
            @"root\CIMV2",
            "SELECT UUID FROM Win32_ComputerSystemProduct");
        var bios = ReadFirst(
            @"root\CIMV2",
            "SELECT SerialNumber, SMBIOSBIOSVersion FROM Win32_BIOS");
        var processor = ReadFirst(
            @"root\CIMV2",
            "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
        return new AgentHardwareSnapshotRequest
        {
            Manufacturer = CleanIdentity(
                GetString(computerSystem, "Manufacturer"),
                150),
            Model = CleanIdentity(
                GetString(computerSystem, "Model"),
                150),
            SerialNumber = CleanIdentity(
                GetString(bios, "SerialNumber"),
                200),
            BiosUuid = CleanIdentity(GetString(product, "UUID"), 100),
            BiosVersion = Limit(
                GetString(bios, "SMBIOSBIOSVersion"),
                200),
            Processor = Limit(GetString(processor, "Name"), 200),
            ProcessorCores = GetInt32(processor, "NumberOfCores"),
            LogicalProcessors =
                GetInt32(processor, "NumberOfLogicalProcessors"),
            MemoryBytes = GetInt64(
                computerSystem,
                "TotalPhysicalMemory"),
        };
    }

    private AgentOperatingSystemSnapshotRequest CollectOperatingSystem()
    {
        var operatingSystem = ReadFirst(
            @"root\CIMV2",
            "SELECT Caption, Version, BuildNumber, OSArchitecture, " +
            "InstallDate, LastBootUpTime FROM Win32_OperatingSystem");
        return new AgentOperatingSystemSnapshotRequest
        {
            Name = Limit(GetString(operatingSystem, "Caption"), 200),
            Version = Limit(GetString(operatingSystem, "Version"), 100),
            Build = Limit(GetString(operatingSystem, "BuildNumber"), 100),
            Architecture = Limit(
                GetString(operatingSystem, "OSArchitecture"),
                50),
            InstalledAtUtc = GetWmiDateTime(
                operatingSystem,
                "InstallDate"),
            LastBootAtUtc = GetWmiDateTime(
                operatingSystem,
                "LastBootUpTime"),
        };
    }

    private AgentSecuritySnapshotRequest CollectSecurity()
    {
        var tpm = ReadFirst(
            @"root\CIMV2\Security\MicrosoftTpm",
            "SELECT IsEnabled_InitialValue, SpecVersion FROM Win32_Tpm");
        var antivirusNames = ReadMany(
                @"root\SecurityCenter2",
                "SELECT displayName FROM AntivirusProduct")
            .Select(item => GetString(item, "displayName"))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
        return new AgentSecuritySnapshotRequest
        {
            SecureBootEnabled = ReadSecureBootEnabled(),
            TpmPresent = tpm is not null,
            TpmEnabled = GetBoolean(tpm, "IsEnabled_InitialValue"),
            TpmVersion = Limit(GetString(tpm, "SpecVersion"), 100),
            SystemDriveProtected = ReadSystemDriveProtection(),
            AntivirusProducts = Limit(
                string.Join(", ", antivirusNames),
                500),
        };
    }

    private AgentNetworkAdapterSnapshotRequest[]
        CollectNetworkAdapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(
                    adapter =>
                        adapter.NetworkInterfaceType !=
                        NetworkInterfaceType.Loopback)
                .OrderBy(adapter => adapter.Name)
                .Take(AgentProtocol.MaximumNetworkAdapters)
                .Select(
                    adapter => new AgentNetworkAdapterSnapshotRequest
                    {
                        Name = Limit(adapter.Name, 150)!,
                        Type = Limit(
                            adapter.NetworkInterfaceType.ToString(),
                            50),
                        MacAddress = FormatMac(
                            adapter.GetPhysicalAddress()),
                        Ipv4Addresses = adapter.GetIPProperties()
                            .UnicastAddresses
                            .Where(
                                address =>
                                    address.Address.AddressFamily ==
                                    AddressFamily.InterNetwork)
                            .Select(address => address.Address.ToString())
                            .Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal)
                            .Take(32)
                            .ToArray(),
                    })
                .ToArray();
        }
        catch (NetworkInformationException exception)
        {
            LogNetworkCollectionFailed(exception);
            return [];
        }
    }

    private List<AgentStorageVolumeSnapshotRequest>
        CollectStorageVolumes()
    {
        var volumes = new List<AgentStorageVolumeSnapshotRequest>();
        foreach (var drive in DriveInfo.GetDrives()
                     .Where(drive => drive.DriveType == DriveType.Fixed)
                     .Take(AgentProtocol.MaximumStorageVolumes))
        {
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                volumes.Add(
                    new AgentStorageVolumeSnapshotRequest
                    {
                        Name = Limit(drive.Name, 20)!,
                        Label = Limit(drive.VolumeLabel, 100),
                        FileSystem = Limit(drive.DriveFormat, 30),
                        TotalBytes = drive.TotalSize,
                        FreeBytes = drive.AvailableFreeSpace,
                    });
            }
            catch (IOException exception)
            {
                LogVolumeCollectionFailed(
                    exception,
                    drive.Name);
            }
        }

        return volumes;
    }

    private AgentInstalledSoftwareSnapshotRequest[]
        CollectInstalledSoftware()
    {
        var software = new Dictionary<
            string,
            AgentInstalledSoftwareSnapshotRequest>(
            StringComparer.OrdinalIgnoreCase);
        CollectInstalledSoftware(
            RegistryView.Registry64,
            "x64",
            software);
        CollectInstalledSoftware(
            RegistryView.Registry32,
            "x86",
            software);
        return software.Values
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Version, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Min(
                AgentProtocol.MaximumInstalledSoftwareItems,
                750))
            .ToArray();
    }

    private void CollectInstalledSoftware(
        RegistryView view,
        string architecture,
        IDictionary<string, AgentInstalledSoftwareSnapshotRequest> target)
    {
        const string uninstallPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                view);
            using var uninstall = localMachine.OpenSubKey(uninstallPath);
            foreach (var subKeyName in uninstall?.GetSubKeyNames() ?? [])
            {
                using var application = uninstall!.OpenSubKey(subKeyName);
                if (application is null)
                {
                    continue;
                }

                var name = Limit(
                    application.GetValue("DisplayName")?.ToString(),
                    300);
                if (name is null ||
                    application.GetValue("SystemComponent") is 1)
                {
                    continue;
                }

                var version = Limit(
                    application.GetValue("DisplayVersion")?.ToString(),
                    100);
                var publisher = Limit(
                    application.GetValue("Publisher")?.ToString(),
                    200);
                var item = new AgentInstalledSoftwareSnapshotRequest
                {
                    Name = name,
                    Version = version,
                    Publisher = publisher,
                    Architecture = architecture,
                    InstalledOn = ParseInstallDate(
                        application.GetValue("InstallDate")?.ToString()),
                };
                var identity = string.Join(
                    '\u001f',
                    name,
                    version,
                    publisher,
                    architecture);
                target.TryAdd(identity, item);
            }
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            LogSoftwareCollectionDenied(
                exception,
                architecture);
        }
    }

    private static string BuildDeviceId()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Cryptography");
        var machineGuid = key?.GetValue("MachineGuid")?.ToString();
        if (string.IsNullOrWhiteSpace(machineGuid))
        {
            throw new InvalidOperationException(
                "Windows MachineGuid is unavailable; a stable device " +
                "identity cannot be created.");
        }

        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(machineGuid.Trim().ToUpperInvariant()));
        return $"win-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string GetHostname()
    {
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var hostname = string.IsNullOrWhiteSpace(properties.HostName)
            ? Environment.MachineName
            : properties.HostName.Trim();
        var domain = properties.DomainName?.Trim().Trim('.');
        var fullyQualified = !string.IsNullOrWhiteSpace(domain) &&
            !hostname.EndsWith(
                "." + domain,
                StringComparison.OrdinalIgnoreCase)
            ? $"{hostname}.{domain}"
            : hostname;
        return Limit(fullyQualified, 150) ?? Environment.MachineName;
    }

    private static string? GetNetworkDomain()
    {
        var domain = IPGlobalProperties
            .GetIPGlobalProperties()
            .DomainName;
        return string.IsNullOrWhiteSpace(domain)
            ? ReadComputerSystemValue("Domain")
            : domain.Trim();
    }

    private WmiRow[] ReadMany(
        string scope,
        string query)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                scope,
                query);
            using var results = searcher.Get();
            return results
                .Cast<ManagementBaseObject>()
                .Select(
                    item => new WmiRow(
                        item.Properties
                            .Cast<PropertyData>()
                            .ToDictionary<PropertyData, string, object?>(
                                property => property.Name,
                                property => property.Value,
                                StringComparer.OrdinalIgnoreCase)))
                .ToArray();
        }
        catch (ManagementException exception)
        {
            LogWmiQueryFailed(
                exception,
                scope);
            return [];
        }
        catch (UnauthorizedAccessException exception)
        {
            LogWmiQueryDenied(
                exception,
                scope);
            return [];
        }
    }

    private WmiRow? ReadFirst(
        string scope,
        string query) =>
        ReadMany(scope, query) is { Length: > 0 } items
            ? items[0]
            : null;

    private bool? ReadSystemDriveProtection()
    {
        var systemDrive = Path.GetPathRoot(
            Environment.GetFolderPath(
                Environment.SpecialFolder.System))
            ?.TrimEnd(Path.DirectorySeparatorChar);
        if (systemDrive is null)
        {
            return null;
        }

        var volume = ReadMany(
                @"root\CIMV2\Security\MicrosoftVolumeEncryption",
                "SELECT DriveLetter, ProtectionStatus " +
                "FROM Win32_EncryptableVolume")
            .FirstOrDefault(
                item =>
                    string.Equals(
                        GetString(item, "DriveLetter"),
                        systemDrive,
                        StringComparison.OrdinalIgnoreCase));
        var protectionStatus = GetInt32(volume, "ProtectionStatus");
        return protectionStatus.HasValue
            ? protectionStatus.Value == 1
            : null;
    }

    private static bool? ReadSecureBootEnabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
        return key?.GetValue("UEFISecureBootEnabled") switch
        {
            int value => value == 1,
            _ => null,
        };
    }

    private static string? ReadComputerSystemValue(string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                $"SELECT {property} FROM Win32_ComputerSystem");
            using var results = searcher.Get();
            return results
                .Cast<ManagementBaseObject>()
                .Select(item => item[property]?.ToString())
                .FirstOrDefault(value => value is not null);
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    private static string? GetString(
        WmiRow? item,
        string property) =>
        GetValue(item, property)?.ToString();

    private static int? GetInt32(
        WmiRow? item,
        string property) =>
        GetValue(item, property) is not { } value
            ? null
            : Convert.ToInt32(
                value,
                CultureInfo.InvariantCulture);

    private static long? GetInt64(
        WmiRow? item,
        string property)
    {
        if (GetValue(item, property) is not { } rawValue ||
            !ulong.TryParse(
                rawValue.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return null;
        }

        return value > long.MaxValue ? long.MaxValue : (long)value;
    }

    private static bool? GetBoolean(
        WmiRow? item,
        string property) =>
        GetValue(item, property) is not { } value
            ? null
            : Convert.ToBoolean(
                value,
                CultureInfo.InvariantCulture);

    private static object? GetValue(WmiRow? item, string property) =>
        item is not null &&
        item.Values.TryGetValue(property, out var value)
            ? value
            : null;

    private static DateTimeOffset? GetWmiDateTime(
        WmiRow? item,
        string property)
    {
        var value = GetString(item, property);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(
                    ManagementDateTimeConverter.ToDateTime(value))
                .ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateOnly? ParseInstallDate(string? value) =>
        DateOnly.TryParseExact(
            value,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;

    private static string? CleanIdentity(string? value, int maximumLength)
    {
        var cleaned = Limit(value, maximumLength);
        return cleaned is null || PlaceholderValues.Contains(cleaned)
            ? null
            : cleaned;
    }

    private static string? Limit(string? value, int maximumLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maximumLength
            ? trimmed
            : trimmed[..maximumLength];
    }

    private static string? FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 0
            ? null
            : string.Join(
                ':',
                bytes.Select(
                    value => value.ToString(
                        "X2",
                        CultureInfo.InvariantCulture)));
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The endpoint inventory collector requires Windows.");
        }
    }

    [LoggerMessage(
        EventId = 2200,
        Level = LogLevel.Warning,
        Message = "Network inventory collection failed.")]
    private partial void LogNetworkCollectionFailed(Exception exception);

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Debug,
        Message = "Storage volume {Volume} could not be inventoried.")]
    private partial void LogVolumeCollectionFailed(
        Exception exception,
        string volume);

    [LoggerMessage(
        EventId = 2202,
        Level = LogLevel.Warning,
        Message = "{Architecture} installed-software inventory was denied.")]
    private partial void LogSoftwareCollectionDenied(
        Exception exception,
        string architecture);

    [LoggerMessage(
        EventId = 2203,
        Level = LogLevel.Debug,
        Message = "WMI inventory query failed in {Scope}.")]
    private partial void LogWmiQueryFailed(
        Exception exception,
        string scope);

    [LoggerMessage(
        EventId = 2204,
        Level = LogLevel.Debug,
        Message = "WMI inventory query was denied in {Scope}.")]
    private partial void LogWmiQueryDenied(
        Exception exception,
        string scope);

    private sealed record WmiRow(
        IReadOnlyDictionary<string, object?> Values);
}
