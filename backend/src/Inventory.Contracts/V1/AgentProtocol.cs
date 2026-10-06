using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inventory.Contracts.V1;

public static class AgentProtocol
{
    public const string EnrollmentPath = "/api/v1/agent/enroll";
    public const string CheckInPath = "/api/v1/agent/check-ins";
    public const string SignatureAlgorithm = "ECDSA_P256_SHA256";
    public const int TimestampWindowSeconds = 300;
    public const int MaximumSnapshotBytes = 1_048_576;
    public const int MaximumInstalledSoftwareItems = 5000;
    public const int MaximumNetworkAdapters = 64;
    public const int MaximumStorageVolumes = 64;

    public static JsonSerializerOptions SnapshotJsonOptions { get; } = CreateJsonOptions();

    public static string HashEnrollmentToken(string token) =>
        Convert.ToBase64String(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(token.Trim())));

    public static string HashIdentity(AgentIdentityRequest identity) =>
        Convert.ToBase64String(
            SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(
                    identity,
                    SnapshotJsonOptions)));

    public static string HashSnapshot(AgentDeviceSnapshotRequest snapshot) =>
        Convert.ToBase64String(
            SHA256.HashData(
                SerializeSnapshot(snapshot)));

    public static byte[] SerializeSnapshot(AgentDeviceSnapshotRequest snapshot) =>
        JsonSerializer.SerializeToUtf8Bytes(snapshot, SnapshotJsonOptions);

    public static string BuildEnrollmentCanonical(
        AgentEnrollmentRequest request)
    {
        return string.Join(
            '\n',
            "INVENTORY-AGENT-ENROLL-V1",
            request.TimestampEpochSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            Encode(request.Nonce),
            Encode(HashEnrollmentToken(request.EnrollmentToken)),
            Encode(request.DeviceId),
            Encode(request.KeyThumbprintSha256.ToLowerInvariant()),
            Encode(request.SignatureAlgorithm.ToUpperInvariant()),
            Encode(request.KeyProtection.ToString()),
            Encode(HashIdentity(request.Identity)));
    }

    public static string BuildCheckInCanonical(AgentCheckInRequest request)
    {
        return string.Join(
            '\n',
            "INVENTORY-AGENT-CHECKIN-V1",
            "POST",
            CheckInPath,
            request.AgentId.ToString("D"),
            Encode(request.DeviceId),
            request.Snapshot.SnapshotId.ToString("D"),
            request.Sequence.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            request.TimestampEpochSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            Encode(request.Nonce),
            Encode(request.BodySha256));
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value.Trim()));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = false,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));
        return options;
    }
}
