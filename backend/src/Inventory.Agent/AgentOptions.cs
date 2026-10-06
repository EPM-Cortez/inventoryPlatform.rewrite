using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace Inventory.Agent;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string ServerBaseUrl { get; set; } = string.Empty;

    public string? EnrollmentTokenFilePath { get; set; }

    public string DeviceKeyName { get; set; } =
        "InventoryAgentSigningKeyV1";

    public bool AllowSoftwareKey { get; set; }

    public bool CollectInstalledSoftware { get; set; } = true;

    public int CheckInIntervalSeconds { get; set; } = 21_600;

    public int EnrollmentRetrySeconds { get; set; } = 60;

    public int HttpTimeoutSeconds { get; set; } = 60;

    public int QueueMaximumItems { get; set; } = 64;

    public int QueueFlushBatchSize { get; set; } = 8;

    public bool DeleteEnrollmentTokenFileAfterEnrollment { get; set; } =
        true;

    public string? DataDirectory { get; set; }

    public void Validate()
    {
        if (!Uri.TryCreate(ServerBaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath is not ("" or "/"))
        {
            throw new OptionsValidationException(
                SectionName,
                typeof(AgentOptions),
                [
                    "Agent:ServerBaseUrl must be an absolute HTTPS origin " +
                    "without credentials, query, fragment or path.",
                ]);
        }

        if (string.IsNullOrWhiteSpace(DeviceKeyName) ||
            DeviceKeyName.Length > 100)
        {
            throw Invalid(
                "Agent:DeviceKeyName must be between 1 and 100 characters.");
        }

        if (CheckInIntervalSeconds is < 300 or > 86_400 ||
            EnrollmentRetrySeconds is < 30 or > 3_600 ||
            HttpTimeoutSeconds is < 5 or > 300 ||
            QueueMaximumItems is < 1 or > 128 ||
            QueueFlushBatchSize is < 1 or > 32 ||
            QueueFlushBatchSize > QueueMaximumItems)
        {
            throw Invalid(
                "Agent interval, timeout and queue settings are outside " +
                "their supported ranges.");
        }

        ValidateLocalAbsolutePath(
            EnrollmentTokenFilePath,
            "Agent:EnrollmentTokenFilePath");
        ValidateLocalAbsolutePath(DataDirectory, "Agent:DataDirectory");
    }

    public string ResolveDataDirectory()
    {
        var configured = string.IsNullOrWhiteSpace(DataDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "Inventory",
                "Agent")
            : DataDirectory.Trim();
        return Path.GetFullPath(configured);
    }

    private static void ValidateLocalAbsolutePath(
        string? value,
        string setting)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var fullPath = Path.GetFullPath(value);
        var root = Path.GetPathRoot(fullPath);
        if (!Path.IsPathFullyQualified(value) ||
            new Uri(fullPath).IsUnc ||
            string.Equals(
                fullPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                root?.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid($"{setting} must be an absolute local path.");
        }
    }

    private static OptionsValidationException Invalid(string message) =>
        new(SectionName, typeof(AgentOptions), [message]);
}

public sealed class AgentRegistryOptionsPostConfigure
    : IPostConfigureOptions<AgentOptions>
{
    private const string RegistryPath = @"SOFTWARE\Inventory\Agent";

    public void PostConfigure(string? name, AgentOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.LocalMachine.OpenSubKey(RegistryPath);
        options.ServerBaseUrl =
            ReadString(key, "ServerBaseUrl") ?? options.ServerBaseUrl;
        options.EnrollmentTokenFilePath =
            ReadString(key, "EnrollmentTokenFilePath") ??
            options.EnrollmentTokenFilePath;
        options.DeviceKeyName =
            ReadString(key, "DeviceKeyName") ?? options.DeviceKeyName;
        options.DataDirectory =
            ReadString(key, "DataDirectory") ?? options.DataDirectory;
        options.AllowSoftwareKey =
            ReadBoolean(key, "AllowSoftwareKey") ??
            options.AllowSoftwareKey;
    }

    private static string? ReadString(RegistryKey? key, string name) =>
        key?.GetValue(name) is string value &&
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static bool? ReadBoolean(RegistryKey? key, string name) =>
        key?.GetValue(name) switch
        {
            int value => value != 0,
            string value when bool.TryParse(value, out var parsed) => parsed,
            string value when int.TryParse(value, out var parsed) =>
                parsed != 0,
            _ => null,
        };
}
