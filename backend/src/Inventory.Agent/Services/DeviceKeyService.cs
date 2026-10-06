using System.Security.Cryptography;
using System.Text;
using Inventory.Contracts.V1;

namespace Inventory.Agent.Services;

public sealed record DeviceKeyMaterial(
    string KeyName,
    AgentKeyProtectionContract Protection,
    string PublicKeyPem,
    string ThumbprintSha256);

public interface IDeviceKeyService
{
    DeviceKeyMaterial EnsureKey(
        string keyName,
        bool allowSoftwareKey,
        AgentKeyProtectionContract? existingProtection);

    string Sign(DeviceKeyMaterial key, string canonical);
}

public sealed partial class DeviceKeyService : IDeviceKeyService
{
    private readonly ILogger<DeviceKeyService> _logger;

    public DeviceKeyService(ILogger<DeviceKeyService> logger)
    {
        _logger = logger;
    }

    public DeviceKeyMaterial EnsureKey(
        string keyName,
        bool allowSoftwareKey,
        AgentKeyProtectionContract? existingProtection)
    {
        EnsureWindows();
        if (existingProtection == AgentKeyProtectionContract.Software &&
            !allowSoftwareKey)
        {
            throw new InvalidOperationException(
                "This agent was enrolled with a software-protected key, but " +
                "software keys are no longer permitted.");
        }

        if (existingProtection.HasValue)
        {
            return OpenOrCreate(
                keyName,
                existingProtection.Value);
        }

        try
        {
            return OpenOrCreate(keyName, AgentKeyProtectionContract.Tpm);
        }
        catch (CryptographicException exception) when (allowSoftwareKey)
        {
            LogSoftwareFallback(
                exception,
                keyName);
            return OpenOrCreate(
                keyName,
                AgentKeyProtectionContract.Software);
        }
    }

    public string Sign(DeviceKeyMaterial key, string canonical)
    {
        EnsureWindows();
        if (string.IsNullOrWhiteSpace(canonical))
        {
            throw new ArgumentException(
                "Canonical request content is required.",
                nameof(canonical));
        }

        using var cngKey = CngKey.Open(
            key.KeyName,
            ResolveProvider(key.Protection),
            CngKeyOpenOptions.MachineKey);
        using var ecdsa = new ECDsaCng(cngKey);
        var signature = ecdsa.SignData(
            Encoding.UTF8.GetBytes(canonical),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Convert.ToBase64String(signature);
    }

    private static DeviceKeyMaterial OpenOrCreate(
        string keyName,
        AgentKeyProtectionContract protection)
    {
        var provider = ResolveProvider(protection);
        using var key = CngKey.Exists(
            keyName,
            provider,
            CngKeyOpenOptions.MachineKey)
            ? CngKey.Open(
                keyName,
                provider,
                CngKeyOpenOptions.MachineKey)
            : CngKey.Create(
                CngAlgorithm.ECDsaP256,
                keyName,
                new CngKeyCreationParameters
                {
                    Provider = provider,
                    KeyCreationOptions =
                        CngKeyCreationOptions.MachineKey,
                    KeyUsage = CngKeyUsages.Signing,
                    ExportPolicy = CngExportPolicies.None,
                });
        using var ecdsa = new ECDsaCng(key);
        if (ecdsa.KeySize != 256)
        {
            throw new CryptographicException(
                "The device signing key is not ECDSA P-256.");
        }

        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
        return new DeviceKeyMaterial(
            keyName,
            protection,
            ecdsa.ExportSubjectPublicKeyInfoPem(),
            Convert.ToHexString(SHA256.HashData(publicKey))
                .ToLowerInvariant());
    }

    private static CngProvider ResolveProvider(
        AgentKeyProtectionContract protection) =>
        protection switch
        {
            AgentKeyProtectionContract.Tpm =>
                CngProvider.MicrosoftPlatformCryptoProvider,
            AgentKeyProtectionContract.Software =>
                CngProvider.MicrosoftSoftwareKeyStorageProvider,
            _ => throw new ArgumentOutOfRangeException(
                nameof(protection),
                protection,
                "Unsupported device key protection."),
        };

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Device signing keys require Windows CNG.");
        }
    }

    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Warning,
        Message = "TPM key creation failed for {KeyName}; using the " +
            "explicitly permitted non-exportable Windows software key provider.")]
    private partial void LogSoftwareFallback(
        Exception exception,
        string keyName);
}
