using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Inventory.Agent.Services;

public interface IEnrollmentTokenStore
{
    Task<string?> ReadAsync(CancellationToken cancellationToken);

    Task DeleteAsync(CancellationToken cancellationToken);
}

public sealed class EnrollmentTokenStore : IEnrollmentTokenStore
{
    internal const string ProtectedFileName = "enrollment-token.bin";

    private const int MinimumTokenCharacters = 20;
    private const int MaximumTokenCharacters = 512;
    private const int MaximumProtectedTokenBytes = 4096;

    private static readonly byte[] Entropy =
        SHA256.HashData(
            Encoding.UTF8.GetBytes(
                "Inventory.Agent.EnrollmentToken.v1.LocalMachine"));

    private readonly IOptionsMonitor<AgentOptions> _options;

    public EnrollmentTokenStore(IOptionsMonitor<AgentOptions> options)
    {
        _options = options;
    }

    public async Task<string?> ReadAsync(
        CancellationToken cancellationToken)
    {
        var protectedPath = ResolveProtectedTokenPath(
            _options.CurrentValue.ResolveDataDirectory());
        if (File.Exists(protectedPath))
        {
            return await ReadProtectedAsync(
                protectedPath,
                cancellationToken);
        }

        return await ReadPlaintextAsync(
            _options.CurrentValue.EnrollmentTokenFilePath,
            cancellationToken);
    }

    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        await DeleteProtectedAsync(
            _options.CurrentValue.ResolveDataDirectory(),
            cancellationToken);

        if (_options.CurrentValue.DeleteEnrollmentTokenFileAfterEnrollment)
        {
            await DeleteFileIfPresentAsync(
                _options.CurrentValue.EnrollmentTokenFilePath,
                cancellationToken);
        }
    }

    internal static Task DeleteProtectedAsync(
        string dataDirectory,
        CancellationToken cancellationToken) =>
        DeleteFileIfPresentAsync(
            ResolveProtectedTokenPath(dataDirectory),
            cancellationToken);

    internal static async Task StageProtectedAsync(
        string token,
        string dataDirectory,
        CancellationToken cancellationToken)
    {
        EnsureWindows();
        var normalizedToken = NormalizeToken(token);
        var protectedPath = ResolveProtectedTokenPath(dataDirectory);
        var directory = Path.GetDirectoryName(protectedPath)!;
        EnsureSafeDataDirectory(directory);

        var clearBytes = Encoding.UTF8.GetBytes(normalizedToken);
        byte[] protectedBytes;
        try
        {
            protectedBytes = ProtectedData.Protect(
                clearBytes,
                Entropy,
                DataProtectionScope.LocalMachine);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }

        if (protectedBytes.Length > MaximumProtectedTokenBytes)
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            throw new InvalidOperationException(
                "The protected enrollment token exceeds its size limit.");
        }

        var temporaryPath = protectedPath + ".new";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options =
                        FileOptions.Asynchronous |
                        FileOptions.WriteThrough,
                }))
            {
                await stream.WriteAsync(
                    protectedBytes,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, protectedPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<string> ReadProtectedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        EnsureSafeTokenFile(path, MaximumProtectedTokenBytes);
        var protectedBytes = await File.ReadAllBytesAsync(
            path,
            cancellationToken);
        byte[] clearBytes;
        try
        {
            clearBytes = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.LocalMachine);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException(
                "The protected enrollment token could not be authenticated.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }

        try
        {
            return NormalizeToken(Encoding.UTF8.GetString(clearBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    private static async Task<string?> ReadPlaintextAsync(
        string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        EnsureSafeTokenFile(path, MaximumTokenCharacters * 4);
        var token = await File.ReadAllTextAsync(path, cancellationToken);
        return NormalizeToken(token);
    }

    private static string NormalizeToken(string token)
    {
        var normalized = token.Trim();
        if (normalized.Length is < MinimumTokenCharacters or
                > MaximumTokenCharacters ||
            normalized.Any(char.IsWhiteSpace) ||
            !(normalized.StartsWith(
                  "inv_enroll.",
                  StringComparison.Ordinal) ||
              normalized.StartsWith(
                  "inv_recover.",
                  StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "The enrollment token has an invalid format.");
        }

        return normalized;
    }

    private static string ResolveProtectedTokenPath(string dataDirectory)
    {
        var directory = Path.GetFullPath(dataDirectory);
        var root = Path.GetPathRoot(directory);
        if (!Path.IsPathFullyQualified(dataDirectory) ||
            new Uri(directory).IsUnc ||
            string.Equals(
                directory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                root?.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The enrollment-token data directory must be an absolute " +
                "local path.");
        }

        return Path.Combine(directory, ProtectedFileName);
    }

    private static void EnsureSafeDataDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        EnsureNotReparsePoint(directory);
    }

    private static void EnsureSafeTokenFile(string path, long maximumBytes)
    {
        EnsureNotReparsePoint(path);
        var length = new FileInfo(path).Length;
        if (length is < 1 || length > maximumBytes)
        {
            throw new InvalidDataException(
                "The enrollment token file has an invalid size.");
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "Enrollment token paths cannot be reparse points.");
        }
    }

    private static Task DeleteFileIfPresentAsync(
        string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Task.CompletedTask;
        }

        return Task.Run(() => File.Delete(path), cancellationToken);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Enrollment token protection requires Windows DPAPI.");
        }
    }
}
