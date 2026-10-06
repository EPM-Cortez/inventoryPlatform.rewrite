using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Agent.Models;
using Inventory.Contracts.V1;
using Microsoft.Extensions.Options;

namespace Inventory.Agent.Services;

public interface IProtectedStateStore
{
    Task<AgentPersistentState> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        AgentPersistentState state,
        CancellationToken cancellationToken);
}

public sealed class ProtectedStateStore : IProtectedStateStore, IDisposable
{
    private const int MaximumProtectedStateBytes =
        (AgentProtocol.MaximumSnapshotBytes * 72) + 65_536;
    private static readonly byte[] Entropy =
        SHA256.HashData(
            Encoding.UTF8.GetBytes(
                "Inventory.Agent.State.v1.LocalMachine"));
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IOptionsMonitor<AgentOptions> _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProtectedStateStore(IOptionsMonitor<AgentOptions> options)
    {
        _options = options;
    }

    public async Task<AgentPersistentState> LoadAsync(
        CancellationToken cancellationToken)
    {
        EnsureWindows();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = ResolveStatePath();
            EnsureSafeDataDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
            {
                return new AgentPersistentState();
            }

            EnsureNotReparsePoint(path);
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length is < 1 or > MaximumProtectedStateBytes)
            {
                throw new InvalidDataException(
                    "The protected agent state has an invalid size.");
            }

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
                    "The protected agent state could not be authenticated.",
                    exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            try
            {
                var state =
                    JsonSerializer.Deserialize<AgentPersistentState>(
                        clearBytes,
                        JsonOptions)
                    ?? throw new InvalidDataException(
                        "The protected agent state is empty.");
                ValidateState(state);
                return state;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The protected agent state is invalid.",
                    exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearBytes);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        AgentPersistentState state,
        CancellationToken cancellationToken)
    {
        EnsureWindows();
        ValidateState(state);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = ResolveStatePath();
            var directory = Path.GetDirectoryName(path)!;
            EnsureSafeDataDirectory(directory);
            var clearBytes = JsonSerializer.SerializeToUtf8Bytes(
                state,
                JsonOptions);
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

            if (protectedBytes.Length > MaximumProtectedStateBytes)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
                throw new InvalidOperationException(
                    "The protected agent state exceeds its size limit.");
            }

            var temporaryPath = path + ".new";
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

                File.Move(temporaryPath, path, overwrite: true);
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
        finally
        {
            _gate.Release();
        }
    }

    private string ResolveStatePath() =>
        Path.Combine(
            _options.CurrentValue.ResolveDataDirectory(),
            "agent-state.bin");

    private static void ValidateState(AgentPersistentState state)
    {
        if (state.SchemaVersion is < 1 or >
            AgentPersistentState.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                "The protected agent state schema is not supported.");
        }

        if ((state.SchemaVersion >= 2 &&
             state.CheckInIntervalSeconds is < 300 or > 86_400) ||
            state.NextSequence < 1 ||
            state.PendingSnapshots.Count > 128 ||
            state.PendingSnapshots.Any(
                item =>
                    item.QueueId == Guid.Empty ||
                    item.Sequence < 1 ||
                    item.Snapshot.SnapshotId == Guid.Empty) ||
            state.PendingSnapshots
                .GroupBy(item => item.Sequence)
                .Any(group => group.Count() > 1) ||
            state.TerminalFailures.Count > 20 ||
            state.TerminalFailures.Any(
                failure =>
                    failure.SnapshotId == Guid.Empty ||
                    failure.Sequence < 1 ||
                    string.IsNullOrWhiteSpace(failure.ProblemType) ||
                    failure.ProblemType.Length > 500) ||
            state.PendingSnapshots.Count > 0 &&
            state.NextSequence <=
            state.PendingSnapshots.Max(item => item.Sequence))
        {
            throw new InvalidDataException(
                "The protected agent state violates queue invariants.");
        }
    }

    private static void EnsureSafeDataDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        EnsureNotReparsePoint(directory);
    }

    private static void EnsureNotReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "Agent state paths cannot be reparse points.");
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The endpoint agent state store requires Windows DPAPI.");
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
