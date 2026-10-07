namespace Inventory.Application.Abstractions;

public enum ImportPreviewPayloadKind
{
    Asset,
    ReferenceData,
    LegacyReport,
}

public interface IImportPreviewBudget
{
    Task<T> AdmitAsync<T>(
        Guid payloadId,
        ImportPreviewPayloadKind kind,
        Guid createdByUserId,
        int payloadBytes,
        DateTimeOffset createdAtUtc,
        Func<CancellationToken, Task<T>> persist,
        CancellationToken cancellationToken);

    Task EnsureAvailableAsync(
        Guid payloadId,
        ImportPreviewPayloadKind kind,
        CancellationToken cancellationToken);

    Task<bool> IsWithinRetentionAsync(
        Guid payloadId,
        ImportPreviewPayloadKind kind,
        CancellationToken cancellationToken);

    Task ReleaseCommittedAsync(
        Guid payloadId,
        ImportPreviewPayloadKind kind,
        CancellationToken cancellationToken);

    Task PreserveLegacyEvidenceAsync(
        Guid runId,
        Guid assetImportJobId,
        CancellationToken cancellationToken);
}
