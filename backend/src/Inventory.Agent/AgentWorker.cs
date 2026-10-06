using System.Security.Cryptography;
using System.Management;
using System.Net.NetworkInformation;
using Inventory.Agent.Models;
using Inventory.Agent.Services;
using Inventory.Contracts.V1;
using Microsoft.Extensions.Options;

namespace Inventory.Agent;

public sealed partial class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private readonly IOptionsMonitor<AgentOptions> _options;
    private readonly IDeviceKeyService _keyService;
    private readonly IDeviceInventoryCollector _collector;
    private readonly IAgentApiClient _apiClient;
    private readonly IEnrollmentTokenStore _enrollmentTokenStore;
    private readonly IProtectedStateStore _stateStore;
    private readonly TimeProvider _timeProvider;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        IOptionsMonitor<AgentOptions> options,
        IDeviceKeyService keyService,
        IDeviceInventoryCollector collector,
        IAgentApiClient apiClient,
        IEnrollmentTokenStore enrollmentTokenStore,
        IProtectedStateStore stateStore,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _options = options;
        _keyService = keyService;
        _collector = collector;
        _apiClient = apiClient;
        _enrollmentTokenStore = enrollmentTokenStore;
        _stateStore = stateStore;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var state = await _stateStore.LoadAsync(stoppingToken);
        if (state.Upgrade(
                _options.CurrentValue.CheckInIntervalSeconds))
        {
            await _stateStore.SaveAsync(state, stoppingToken);
        }

        var key = _keyService.EnsureKey(
            _options.CurrentValue.DeviceKeyName,
            _options.CurrentValue.AllowSoftwareKey,
            state.KeyProtection);
        ValidatePersistedKey(state, key);
        if (state.IsEnrolled)
        {
            await _enrollmentTokenStore.DeleteAsync(stoppingToken);
        }

        LogStarted(
            key.Protection,
            _options.CurrentValue.ServerBaseUrl);

        var enrollmentAttempt = 0;
        DateTimeOffset? nextCollectionAtUtc = state.IsEnrolled
            ? _timeProvider.GetUtcNow() +
              AgentSchedule.ComputeInitialCollectionDelay(
                  state.DeviceId!,
                  state.CheckInIntervalSeconds)
            : null;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!state.IsEnrolled)
            {
                var enrollment = await TryEnrollAsync(
                        state,
                        key,
                        stoppingToken);
                if (!enrollment.Enrolled)
                {
                    enrollmentAttempt += 1;
                    var delay =
                        AgentSchedule.ComputeEnrollmentRetryDelay(
                            key.ThumbprintSha256,
                            enrollmentAttempt,
                            _options.CurrentValue
                                .EnrollmentRetrySeconds,
                            enrollment.RetryAfter);
                    LogEnrollmentRetryScheduled(
                        enrollmentAttempt,
                        delay.TotalSeconds,
                        enrollment.Retryable);
                    await DelayAsync(delay, stoppingToken);
                    continue;
                }

                enrollmentAttempt = 0;
                nextCollectionAtUtc =
                    _timeProvider.GetUtcNow() +
                    AgentSchedule.ComputeInitialCollectionDelay(
                        state.DeviceId!,
                        state.CheckInIntervalSeconds);
                continue;
            }

            var flush = await FlushQueueAsync(
                state,
                key,
                stoppingToken);
            var now = _timeProvider.GetUtcNow();
            nextCollectionAtUtc = ResolveNextCollectionAtUtc(
                now,
                nextCollectionAtUtc,
                state,
                flush);
            if (nextCollectionAtUtc <= now)
            {
                if (state.PendingSnapshots.Count <
                    _options.CurrentValue.QueueMaximumItems)
                {
                    try
                    {
                        state.PendingSnapshots.Add(
                            new PendingAgentSnapshot
                            {
                                Sequence = state.NextSequence,
                                Snapshot = _collector.CollectSnapshot(),
                                QueuedAtUtc = now,
                                NextAttemptAtUtc = now,
                            });
                        state.NextSequence += 1;
                        await _stateStore.SaveAsync(
                            state,
                            stoppingToken);
                        flush = await FlushQueueAsync(
                            state,
                            key,
                            stoppingToken);
                    }
                    catch (Exception exception)
                        when (exception is ManagementException or
                            NetworkInformationException or IOException)
                    {
                        LogCollectionFailed(exception);
                    }
                }
                else
                {
                    LogQueueFull(state.PendingSnapshots.Count);
                }

                nextCollectionAtUtc =
                    _timeProvider.GetUtcNow() +
                    TimeSpan.FromSeconds(
                        state.CheckInIntervalSeconds);
            }

            var wakeAtUtc = flush.NextAttemptAtUtc.HasValue &&
                flush.NextAttemptAtUtc < nextCollectionAtUtc
                    ? flush.NextAttemptAtUtc.Value
                    : nextCollectionAtUtc.Value;
            await DelayUntilAsync(wakeAtUtc, stoppingToken);
        }
    }

    private async Task<EnrollmentAttemptResult> TryEnrollAsync(
        AgentPersistentState state,
        DeviceKeyMaterial key,
        CancellationToken cancellationToken)
    {
        var token = await _enrollmentTokenStore.ReadAsync(cancellationToken);
        if (token is null)
        {
            LogEnrollmentTokenMissing();
            return new EnrollmentAttemptResult(
                false,
                true,
                null);
        }

        var identity = _collector.CollectIdentity();
        var unsigned = new AgentEnrollmentRequest
        {
            EnrollmentToken = token,
            TimestampEpochSeconds =
                _timeProvider.GetUtcNow().ToUnixTimeSeconds(),
            Nonce = CreateNonce(),
            DeviceId = identity.DeviceId,
            PublicKeyPem = key.PublicKeyPem,
            SignatureAlgorithm = AgentProtocol.SignatureAlgorithm,
            KeyProtection = key.Protection,
            KeyThumbprintSha256 = key.ThumbprintSha256,
            Identity = identity.Identity,
            Signature = string.Empty,
        };
        var request = new AgentEnrollmentRequest
        {
            EnrollmentToken = unsigned.EnrollmentToken,
            TimestampEpochSeconds = unsigned.TimestampEpochSeconds,
            Nonce = unsigned.Nonce,
            DeviceId = unsigned.DeviceId,
            PublicKeyPem = unsigned.PublicKeyPem,
            SignatureAlgorithm = unsigned.SignatureAlgorithm,
            KeyProtection = unsigned.KeyProtection,
            KeyThumbprintSha256 = unsigned.KeyThumbprintSha256,
            Identity = unsigned.Identity,
            Signature = _keyService.Sign(
                key,
                AgentProtocol.BuildEnrollmentCanonical(unsigned)),
        };
        var result = await _apiClient.EnrollAsync(
            request,
            cancellationToken);
        if (!result.Success || result.Value is null)
        {
            LogEnrollmentFailed(
                result.Retryable,
                result.ProblemType,
                result.Detail);
            return new EnrollmentAttemptResult(
                false,
                result.Retryable,
                result.RetryAfter);
        }

        ApplyEnrollmentResponse(
            state,
            key,
            identity,
            result.Value,
            _timeProvider.GetUtcNow());
        await _stateStore.SaveAsync(state, cancellationToken);
        await _enrollmentTokenStore.DeleteAsync(cancellationToken);
        LogEnrolled(result.Value.AgentId, result.Value.DeviceId);
        return new EnrollmentAttemptResult(true, false, null);
    }

    internal static void ApplyEnrollmentResponse(
        AgentPersistentState state,
        DeviceKeyMaterial key,
        CollectedAgentIdentity identity,
        AgentEnrollmentResponse response,
        DateTimeOffset now)
    {
        if (response.AgentId == Guid.Empty ||
            !string.Equals(
                response.DeviceId,
                identity.DeviceId,
                StringComparison.Ordinal) ||
            response.NextSequence < 1 ||
            response.CheckInIntervalSeconds is < 300 or > 86_400)
        {
            throw new InvalidDataException(
                "The inventory server returned inconsistent enrollment data.");
        }

        state.AgentId = response.AgentId;
        state.DeviceId = response.DeviceId;
        state.KeyThumbprintSha256 = key.ThumbprintSha256;
        state.KeyProtection = key.Protection;
        state.EnrolledAtUtc = now;
        state.CheckInIntervalSeconds =
            response.CheckInIntervalSeconds;
        state.NextSequence = response.NextSequence;
    }

    internal async Task<QueueFlushResult> FlushQueueAsync(
        AgentPersistentState state,
        DeviceKeyMaterial key,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        int? updatedCheckInIntervalSeconds = null;
        while (processed < _options.CurrentValue.QueueFlushBatchSize)
        {
            var item = state.PendingSnapshots
                .OrderBy(candidate => candidate.Sequence)
                .FirstOrDefault();
            if (item is null)
            {
                return new QueueFlushResult(
                    null,
                    updatedCheckInIntervalSeconds);
            }

            var now = _timeProvider.GetUtcNow();
            if (item.NextAttemptAtUtc > now)
            {
                return new QueueFlushResult(
                    item.NextAttemptAtUtc,
                    updatedCheckInIntervalSeconds);
            }

            var unsigned = new AgentCheckInRequest
            {
                AgentId = state.AgentId!.Value,
                DeviceId = state.DeviceId!,
                Sequence = item.Sequence,
                TimestampEpochSeconds =
                    now.ToUnixTimeSeconds(),
                Nonce = CreateNonce(),
                BodySha256 = AgentProtocol.HashSnapshot(item.Snapshot),
                Signature = string.Empty,
                Snapshot = item.Snapshot,
            };
            var request = new AgentCheckInRequest
            {
                AgentId = unsigned.AgentId,
                DeviceId = unsigned.DeviceId,
                Sequence = unsigned.Sequence,
                TimestampEpochSeconds = unsigned.TimestampEpochSeconds,
                Nonce = unsigned.Nonce,
                BodySha256 = unsigned.BodySha256,
                Signature = _keyService.Sign(
                    key,
                    AgentProtocol.BuildCheckInCanonical(unsigned)),
                Snapshot = unsigned.Snapshot,
            };
            var result = await _apiClient.CheckInAsync(
                request,
                cancellationToken);
            if (result.Success &&
                result.Value is
                {
                    Accepted: true,
                } response &&
                response.AgentId == state.AgentId &&
                response.SnapshotId == item.Snapshot.SnapshotId)
            {
                var previousCheckInIntervalSeconds =
                    state.CheckInIntervalSeconds;
                var supersededCount = ApplyAcceptedResponse(
                    state,
                    item,
                    response,
                    now);
                if (state.CheckInIntervalSeconds !=
                    previousCheckInIntervalSeconds)
                {
                    updatedCheckInIntervalSeconds =
                        state.CheckInIntervalSeconds;
                }

                await _stateStore.SaveAsync(state, cancellationToken);
                LogCheckInSucceeded(
                    item.Snapshot.SnapshotId,
                    item.Sequence,
                    response.Duplicate);
                if (supersededCount > 0)
                {
                    LogServerSequenceAdvanced(
                        response.NextSequence,
                        supersededCount);
                }

                processed += 1;
                continue;
            }

            if (result.Success)
            {
                throw new InvalidDataException(
                    "The inventory server returned inconsistent check-in data.");
            }

            if (result.Retryable)
            {
                item.AttemptCount += 1;
                item.NextAttemptAtUtc =
                    now +
                    AgentSchedule.ComputeCheckInRetryDelay(
                        state.DeviceId!,
                        item.Sequence,
                        item.AttemptCount,
                        result.RetryAfter);
                await _stateStore.SaveAsync(state, cancellationToken);
                LogCheckInRetry(
                    item.Snapshot.SnapshotId,
                    item.AttemptCount,
                    result.ProblemType);
                return new QueueFlushResult(
                    item.NextAttemptAtUtc,
                    updatedCheckInIntervalSeconds);
            }

            var problemType =
                result.ProblemType ?? "agent_check_in_rejected";
            state.PendingSnapshots.Remove(item);
            RecordTerminalFailure(state, item, now, problemType);
            await _stateStore.SaveAsync(state, cancellationToken);
            LogCheckInRejected(
                item.Snapshot.SnapshotId,
                item.Sequence,
                result.ProblemType,
                result.Detail);
            processed += 1;
        }

        var next = state.PendingSnapshots
            .OrderBy(item => item.Sequence)
            .FirstOrDefault();
        return new QueueFlushResult(
            next is null
                ? null
                : next.NextAttemptAtUtc > _timeProvider.GetUtcNow()
                    ? next.NextAttemptAtUtc
                    : _timeProvider.GetUtcNow().AddSeconds(1),
            updatedCheckInIntervalSeconds);
    }

    internal static int ApplyAcceptedResponse(
        AgentPersistentState state,
        PendingAgentSnapshot item,
        AgentCheckInResponse response,
        DateTimeOffset now)
    {
        var expectedNextSequence = checked(item.Sequence + 1);
        if (response.CheckInIntervalSeconds is < 300 or > 86_400)
        {
            throw new InvalidDataException(
                "The inventory server returned an invalid check-in interval.");
        }

        if (response.NextSequence < expectedNextSequence ||
            !response.Duplicate &&
            response.NextSequence != expectedNextSequence)
        {
            throw new InvalidDataException(
                "The inventory server returned an invalid next sequence.");
        }

        if (!state.PendingSnapshots.Remove(item))
        {
            throw new InvalidDataException(
                "The accepted snapshot was not present in the local queue.");
        }

        var superseded = state.PendingSnapshots
            .Where(candidate =>
                candidate.Sequence < response.NextSequence)
            .OrderBy(candidate => candidate.Sequence)
            .ToArray();
        foreach (var candidate in superseded)
        {
            state.PendingSnapshots.Remove(candidate);
            RecordTerminalFailure(
                state,
                candidate,
                now,
                "agent_sequence_superseded_by_server");
        }

        state.NextSequence = Math.Max(
            state.NextSequence,
            response.NextSequence);
        state.CheckInIntervalSeconds =
            response.CheckInIntervalSeconds;
        return superseded.Length;
    }

    internal static DateTimeOffset ResolveNextCollectionAtUtc(
        DateTimeOffset now,
        DateTimeOffset? scheduledCollectionAtUtc,
        AgentPersistentState state,
        QueueFlushResult flush)
    {
        if (flush.UpdatedCheckInIntervalSeconds is { } intervalSeconds)
        {
            return now + TimeSpan.FromSeconds(intervalSeconds);
        }

        return scheduledCollectionAtUtc ??
            now +
            AgentSchedule.ComputeInitialCollectionDelay(
                state.DeviceId!,
                state.CheckInIntervalSeconds);
    }

    private static void RecordTerminalFailure(
        AgentPersistentState state,
        PendingAgentSnapshot item,
        DateTimeOffset now,
        string problemType)
    {
        state.TerminalFailures.Add(
            new AgentTerminalFailure
            {
                SnapshotId = item.Snapshot.SnapshotId,
                Sequence = item.Sequence,
                FailedAtUtc = now,
                ProblemType = problemType.Length <= 500
                    ? problemType
                    : problemType[..500],
            });
        while (state.TerminalFailures.Count > 20)
        {
            state.TerminalFailures.RemoveAt(0);
        }
    }

    private static void ValidatePersistedKey(
        AgentPersistentState state,
        DeviceKeyMaterial key)
    {
        if (!state.IsEnrolled)
        {
            return;
        }

        if (state.KeyProtection != key.Protection ||
            !string.Equals(
                state.KeyThumbprintSha256,
                key.ThumbprintSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The enrolled device key no longer matches protected agent " +
                "state. Administrative re-enrollment is required.");
        }
    }

    private static string CreateNonce() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private Task DelayUntilAsync(
        DateTimeOffset wakeAtUtc,
        CancellationToken cancellationToken)
    {
        var delay = wakeAtUtc - _timeProvider.GetUtcNow();
        return DelayAsync(
            delay > TimeSpan.Zero ? delay : TimeSpan.Zero,
            cancellationToken);
    }

    private Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken) =>
        Task.Delay(delay, _timeProvider, cancellationToken);

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Inventory agent started. KeyProtection={KeyProtection}, Server={Server}")]
    private partial void LogStarted(
        AgentKeyProtectionContract keyProtection,
        string server);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Enrollment bootstrap token is unavailable; enrollment will retry.")]
    private partial void LogEnrollmentTokenMissing();

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Enrollment failed. Retryable={Retryable}, ProblemType={ProblemType}, Detail={Detail}")]
    private partial void LogEnrollmentFailed(
        bool retryable,
        string? problemType,
        string? detail);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Information,
        Message = "Agent enrolled. AgentId={AgentId}, DeviceId={DeviceId}")]
    private partial void LogEnrolled(Guid agentId, string deviceId);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Information,
        Message = "Snapshot accepted. SnapshotId={SnapshotId}, Sequence={Sequence}, Duplicate={Duplicate}")]
    private partial void LogCheckInSucceeded(
        Guid snapshotId,
        long sequence,
        bool duplicate);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Warning,
        Message = "Snapshot retry scheduled. SnapshotId={SnapshotId}, Attempt={Attempt}, ProblemType={ProblemType}")]
    private partial void LogCheckInRetry(
        Guid snapshotId,
        int attempt,
        string? problemType);

    [LoggerMessage(
        EventId = 2006,
        Level = LogLevel.Error,
        Message = "Snapshot permanently rejected. SnapshotId={SnapshotId}, Sequence={Sequence}, ProblemType={ProblemType}, Detail={Detail}")]
    private partial void LogCheckInRejected(
        Guid snapshotId,
        long sequence,
        string? problemType,
        string? detail);

    [LoggerMessage(
        EventId = 2007,
        Level = LogLevel.Warning,
        Message = "Inventory collection failed; the next cycle will retry.")]
    private partial void LogCollectionFailed(Exception exception);

    [LoggerMessage(
        EventId = 2008,
        Level = LogLevel.Warning,
        Message = "Offline snapshot queue is full at {Count}; collection is paused until delivery succeeds.")]
    private partial void LogQueueFull(int count);

    [LoggerMessage(
        EventId = 2009,
        Level = LogLevel.Information,
        Message = "Enrollment retry {Attempt} scheduled in {DelaySeconds} seconds. Retryable={Retryable}")]
    private partial void LogEnrollmentRetryScheduled(
        int attempt,
        double delaySeconds,
        bool retryable);

    [LoggerMessage(
        EventId = 2010,
        Level = LogLevel.Warning,
        Message = "Server advanced the expected sequence to {NextSequence}; {SupersededCount} stale queued snapshot(s) were quarantined.")]
    private partial void LogServerSequenceAdvanced(
        long nextSequence,
        int supersededCount);

    private sealed record EnrollmentAttemptResult(
        bool Enrolled,
        bool Retryable,
        TimeSpan? RetryAfter);
}

internal sealed record QueueFlushResult(
    DateTimeOffset? NextAttemptAtUtc,
    int? UpdatedCheckInIntervalSeconds);
