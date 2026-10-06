using Inventory.Agent.Models;
using Inventory.Agent.Services;
using Inventory.Contracts.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Inventory.Agent.Tests;

public sealed class AgentWorkerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BackedOffOldestSnapshotBlocksNewerReadySnapshot()
    {
        var api = new RecordingApiClient();
        var store = new RecordingStateStore();
        var worker = CreateWorker(api, store);
        var state = CreateEnrolledState(
            CreatePending(1, Now.AddMinutes(5)),
            CreatePending(2, Now));

        var result = await worker.FlushQueueAsync(
            state,
            CreateKey(),
            TestContext.Current.CancellationToken);

        Assert.Empty(api.CheckIns);
        Assert.Equal(Now.AddMinutes(5), result.NextAttemptAtUtc);
        Assert.Equal([1L, 2L], state.PendingSnapshots
            .Select(item => item.Sequence));
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RetryableHeadHonoursRetryAfterAndBlocksLaterItem()
    {
        var retryAfter = TimeSpan.FromMinutes(10);
        var api = new RecordingApiClient
        {
            CheckInResult = new AgentApiResult<AgentCheckInResponse>(
                false,
                true,
                null,
                "rate_limited",
                "Retry later.",
                retryAfter),
        };
        var store = new RecordingStateStore();
        var worker = CreateWorker(api, store);
        var state = CreateEnrolledState(
            CreatePending(1, Now),
            CreatePending(2, Now));

        var result = await worker.FlushQueueAsync(
            state,
            CreateKey(),
            TestContext.Current.CancellationToken);

        var request = Assert.Single(api.CheckIns);
        Assert.Equal(1, request.Sequence);
        Assert.Equal(1, state.PendingSnapshots[0].AttemptCount);
        Assert.Equal(
            Now + retryAfter,
            state.PendingSnapshots[0].NextAttemptAtUtc);
        Assert.Equal(
            Now + retryAfter,
            result.NextAttemptAtUtc);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void DuplicateResponseAdvancesSequenceWithoutOverwritingQueue()
    {
        var state = CreateEnrolledState(
            CreatePending(1, Now),
            CreatePending(2, Now),
            CreatePending(3, Now));
        state.NextSequence = 4;
        var accepted = state.PendingSnapshots[0];
        var response = new AgentCheckInResponse(
            true,
            true,
            state.AgentId!.Value,
            accepted.Snapshot.SnapshotId,
            Now,
            3,
            null,
            7_200);

        var superseded = AgentWorker.ApplyAcceptedResponse(
            state,
            accepted,
            response,
            Now);

        Assert.Equal(1, superseded);
        Assert.Equal(
            [3L],
            state.PendingSnapshots.Select(item => item.Sequence));
        var failure = Assert.Single(state.TerminalFailures);
        Assert.Equal(2, failure.Sequence);
        Assert.Equal(
            "agent_sequence_superseded_by_server",
            failure.ProblemType);
        Assert.Equal(4, state.NextSequence);
        Assert.Equal(7_200, state.CheckInIntervalSeconds);
    }

    [Fact]
    public void NonDuplicateResponseCannotSkipASequence()
    {
        var state = CreateEnrolledState(CreatePending(1, Now));
        state.NextSequence = 2;
        var accepted = state.PendingSnapshots[0];
        var response = new AgentCheckInResponse(
            true,
            false,
            state.AgentId!.Value,
            accepted.Snapshot.SnapshotId,
            Now,
            3,
            null,
            21_600);

        Assert.Throws<InvalidDataException>(
            () => AgentWorker.ApplyAcceptedResponse(
                state,
                accepted,
                response,
                Now));
        Assert.Single(state.PendingSnapshots);
        Assert.Equal(2, state.NextSequence);
    }

    [Fact]
    public async Task AcceptedCheckInPersistsAndReschedulesUpdatedCadence()
    {
        var state = CreateEnrolledState(CreatePending(1, Now));
        state.NextSequence = 2;
        var accepted = state.PendingSnapshots[0];
        var api = new RecordingApiClient
        {
            CheckInResult = new AgentApiResult<AgentCheckInResponse>(
                true,
                false,
                new AgentCheckInResponse(
                    true,
                    false,
                    state.AgentId!.Value,
                    accepted.Snapshot.SnapshotId,
                    Now,
                    2,
                    null,
                    3_600),
                null,
                null,
                null),
        };
        var store = new RecordingStateStore();
        var worker = CreateWorker(api, store);

        var flush = await worker.FlushQueueAsync(
            state,
            CreateKey(),
            TestContext.Current.CancellationToken);
        var scheduled = AgentWorker.ResolveNextCollectionAtUtc(
            Now,
            Now.AddHours(6),
            state,
            flush);

        Assert.Empty(state.PendingSnapshots);
        Assert.Equal(3_600, state.CheckInIntervalSeconds);
        Assert.Equal(3_600, flush.UpdatedCheckInIntervalSeconds);
        Assert.Equal(Now.AddHours(1), scheduled);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void InvalidCheckInCadenceDoesNotMutateQueueOrState()
    {
        var state = CreateEnrolledState(CreatePending(1, Now));
        state.NextSequence = 2;
        var accepted = state.PendingSnapshots[0];
        var response = new AgentCheckInResponse(
            true,
            false,
            state.AgentId!.Value,
            accepted.Snapshot.SnapshotId,
            Now,
            2,
            null,
            299);

        Assert.Throws<InvalidDataException>(
            () => AgentWorker.ApplyAcceptedResponse(
                state,
                accepted,
                response,
                Now));
        Assert.Single(state.PendingSnapshots);
        Assert.Equal(21_600, state.CheckInIntervalSeconds);
        Assert.Equal(2, state.NextSequence);
    }

    [Fact]
    public void SchedulesAreStableExponentialAndHonourRetryAfter()
    {
        var firstInitial =
            AgentSchedule.ComputeInitialCollectionDelay(
                "device-01",
                21_600);
        var secondInitial =
            AgentSchedule.ComputeInitialCollectionDelay(
                "device-01",
                21_600);
        Assert.Equal(firstInitial, secondInitial);
        Assert.InRange(
            firstInitial,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(15));

        var firstRetry =
            AgentSchedule.ComputeEnrollmentRetryDelay(
                "thumbprint",
                1,
                60,
                null);
        var fourthRetry =
            AgentSchedule.ComputeEnrollmentRetryDelay(
                "thumbprint",
                4,
                60,
                null);
        Assert.True(fourthRetry > firstRetry);

        var retryAfter = TimeSpan.FromMinutes(20);
        Assert.Equal(
            retryAfter,
            AgentSchedule.ComputeCheckInRetryDelay(
                "device-01",
                1,
                1,
                retryAfter));
    }

    [Fact]
    public void VersionOneStateUsesConfiguredIntervalWhenUpgraded()
    {
        var state = new AgentPersistentState
        {
            SchemaVersion = 1,
            CheckInIntervalSeconds = 21_600,
        };

        var upgraded = state.Upgrade(7_200);

        Assert.True(upgraded);
        Assert.Equal(
            AgentPersistentState.CurrentSchemaVersion,
            state.SchemaVersion);
        Assert.Equal(7_200, state.CheckInIntervalSeconds);
        Assert.False(state.Upgrade(3_600));
    }

    [Fact]
    public void EnrollmentPersistsServerAdvertisedInterval()
    {
        var state = new AgentPersistentState();
        var key = CreateKey();
        var identity = new CollectedAgentIdentity(
            "device-01",
            new AgentIdentityRequest());
        var response = new AgentEnrollmentResponse(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            identity.DeviceId,
            Now,
            7,
            7_200);

        AgentWorker.ApplyEnrollmentResponse(
            state,
            key,
            identity,
            response,
            Now);

        Assert.True(state.IsEnrolled);
        Assert.Equal(7_200, state.CheckInIntervalSeconds);
        Assert.Equal(7, state.NextSequence);
        Assert.Equal(Now, state.EnrolledAtUtc);
    }

    private static AgentWorker CreateWorker(
        IAgentApiClient apiClient,
        IProtectedStateStore stateStore) =>
        new(
            NullLogger<AgentWorker>.Instance,
            new StaticOptionsMonitor<AgentOptions>(
                new AgentOptions
                {
                    ServerBaseUrl = "https://inventory.example.test",
                }),
            new StubDeviceKeyService(),
            new StubInventoryCollector(),
            apiClient,
            new StubEnrollmentTokenStore(),
            stateStore,
            new FixedTimeProvider(Now));

    private static AgentPersistentState CreateEnrolledState(
        params PendingAgentSnapshot[] pending) =>
        new()
        {
            AgentId = Guid.Parse(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            DeviceId = "device-01",
            KeyThumbprintSha256 = "thumbprint",
            KeyProtection = AgentKeyProtectionContract.Software,
            EnrolledAtUtc = Now.AddDays(-1),
            NextSequence = pending.Length + 1,
            PendingSnapshots = [.. pending],
        };

    private static PendingAgentSnapshot CreatePending(
        long sequence,
        DateTimeOffset nextAttemptAtUtc) =>
        new()
        {
            Sequence = sequence,
            QueuedAtUtc = Now,
            NextAttemptAtUtc = nextAttemptAtUtc,
            Snapshot = new AgentDeviceSnapshotRequest
            {
                SnapshotId = Guid.NewGuid(),
                CollectedAtUtc = Now,
                AgentVersion = "1.0.0",
                Hostname = "TEST-PC",
            },
        };

    private static DeviceKeyMaterial CreateKey() =>
        new(
            "test-key",
            AgentKeyProtectionContract.Software,
            "public-key",
            "thumbprint");

    private sealed class StubEnrollmentTokenStore
        : IEnrollmentTokenStore
    {
        public Task<string?> ReadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task DeleteAsync(
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaticOptionsMonitor<T>(T value)
        : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(
            Action<T, string?> listener) => null;
    }

    private sealed class StubDeviceKeyService : IDeviceKeyService
    {
        public DeviceKeyMaterial EnsureKey(
            string keyName,
            bool allowSoftwareKey,
            AgentKeyProtectionContract? existingProtection) =>
            CreateKey();

        public string Sign(
            DeviceKeyMaterial key,
            string canonical) => "signature";
    }

    private sealed class StubInventoryCollector
        : IDeviceInventoryCollector
    {
        public CollectedAgentIdentity CollectIdentity() =>
            throw new NotSupportedException();

        public AgentDeviceSnapshotRequest CollectSnapshot() =>
            throw new NotSupportedException();
    }

    private sealed class RecordingApiClient : IAgentApiClient
    {
        public List<AgentCheckInRequest> CheckIns { get; } = [];

        public AgentApiResult<AgentCheckInResponse> CheckInResult
        {
            get;
            init;
        } = new(
            false,
            true,
            null,
            "unavailable",
            "Unavailable.",
            null);

        public Task<AgentApiResult<AgentEnrollmentResponse>>
            EnrollAsync(
                AgentEnrollmentRequest request,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentApiResult<AgentCheckInResponse>>
            CheckInAsync(
                AgentCheckInRequest request,
                CancellationToken cancellationToken)
        {
            CheckIns.Add(request);
            return Task.FromResult(CheckInResult);
        }
    }

    private sealed class RecordingStateStore : IProtectedStateStore
    {
        public int SaveCount { get; private set; }

        public Task<AgentPersistentState> LoadAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            AgentPersistentState state,
            CancellationToken cancellationToken)
        {
            SaveCount += 1;
            return Task.CompletedTask;
        }
    }
}
