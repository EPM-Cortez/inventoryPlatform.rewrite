using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException(
        "Release attestation signing requires the Windows code-signing certificate store.");
}

if (args.Length != 3 ||
    args[0].Length != 40 ||
    args[0].Any(character => !char.IsAsciiHexDigit(character)) ||
    !Path.IsPathFullyQualified(args[1]) ||
    !Path.IsPathFullyQualified(args[2]))
{
    throw new ArgumentException(
        "Expected a SHA-1 certificate thumbprint, absolute manifest path, and absolute output directory.");
}

using var certificate = FindCertificate(args[0]);
using var rsa = certificate.GetRSAPrivateKey()
    ?? throw new InvalidOperationException(
        "The code-signing certificate needs an accessible RSA private key.");

var manifestBytes = await File.ReadAllBytesAsync(args[1]);
if (manifestBytes.Length is < 1 or > 1_048_576)
{
    throw new InvalidOperationException("The release manifest size is invalid.");
}

var signature = rsa.SignData(
    manifestBytes,
    HashAlgorithmName.SHA256,
    RSASignaturePadding.Pkcs1);
var outputDirectory = Path.GetFullPath(args[2]);
if (!Directory.Exists(outputDirectory))
{
    throw new DirectoryNotFoundException(outputDirectory);
}

await File.WriteAllBytesAsync(
    Path.Combine(outputDirectory, "agent-artifacts.json.sig"),
    signature);
await File.WriteAllTextAsync(
    Path.Combine(outputDirectory, "agent-release-certificate.pem"),
    certificate.ExportCertificatePem(),
    new UTF8Encoding(false));

static X509Certificate2 FindCertificate(string thumbprint)
{
    foreach (var location in new[]
             {
                 StoreLocation.CurrentUser,
                 StoreLocation.LocalMachine,
             })
    {
        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly);
        var matches = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            thumbprint,
            validOnly: false);
        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                "The code-signing thumbprint matched multiple certificates.");
        }
    }

    throw new InvalidOperationException(
        "The code-signing certificate was not found in a Windows certificate store.");
}
