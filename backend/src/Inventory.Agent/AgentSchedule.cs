using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Inventory.Agent;

internal static class AgentSchedule
{
    private const int MaximumInitialJitterSeconds = 900;
    private const int MaximumEnrollmentRetrySeconds = 3600;
    private const int MaximumCheckInRetrySeconds = 1800;
    private const int MaximumRetryAfterSeconds = 3600;

    public static TimeSpan ComputeInitialCollectionDelay(
        string deviceId,
        int checkInIntervalSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var jitterWindowSeconds = Math.Clamp(
            checkInIntervalSeconds / 12,
            1,
            MaximumInitialJitterSeconds);
        return TimeSpan.FromSeconds(
            StableOffset(
                $"initial-collection:{deviceId}",
                jitterWindowSeconds + 1));
    }

    public static TimeSpan ComputeEnrollmentRetryDelay(
        string keyThumbprintSha256,
        int attempt,
        int baseRetrySeconds,
        TimeSpan? retryAfter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyThumbprintSha256);
        return ComputeRetryDelay(
            $"enrollment:{keyThumbprintSha256}",
            attempt,
            baseRetrySeconds,
            MaximumEnrollmentRetrySeconds,
            retryAfter);
    }

    public static TimeSpan ComputeCheckInRetryDelay(
        string deviceId,
        long sequence,
        int attempt,
        TimeSpan? retryAfter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        return ComputeRetryDelay(
            $"check-in:{deviceId}:{sequence}",
            attempt,
            15,
            MaximumCheckInRetrySeconds,
            retryAfter);
    }

    private static TimeSpan ComputeRetryDelay(
        string identity,
        int attempt,
        int baseRetrySeconds,
        int maximumBackoffSeconds,
        TimeSpan? retryAfter)
    {
        var boundedAttempt = Math.Clamp(attempt, 1, 30);
        var multiplier = 1L << Math.Min(boundedAttempt - 1, 30);
        var exponentialSeconds = (int)Math.Min(
            maximumBackoffSeconds,
            (long)baseRetrySeconds * multiplier);
        var jitterWindowSeconds = Math.Clamp(
            exponentialSeconds / 4,
            1,
            60);
        var jitterSeconds = StableOffset(
            $"{identity}:{boundedAttempt}",
            jitterWindowSeconds + 1);
        var computedSeconds = Math.Min(
            maximumBackoffSeconds,
            exponentialSeconds + jitterSeconds);
        var advertisedSeconds = retryAfter.HasValue
            ? Math.Clamp(
                (int)Math.Ceiling(
                    Math.Max(0, retryAfter.Value.TotalSeconds)),
                0,
                MaximumRetryAfterSeconds)
            : 0;
        return TimeSpan.FromSeconds(
            Math.Max(computedSeconds, advertisedSeconds));
    }

    private static int StableOffset(string input, int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 1)
        {
            return 0;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var value = BinaryPrimitives.ReadUInt32BigEndian(hash);
        return (int)(value % (uint)exclusiveMaximum);
    }
}
