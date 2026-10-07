using System.Data;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Auditing;
using Inventory.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Inventory.Infrastructure.Data;

public sealed class EfImportPreviewBudget : IImportPreviewBudget
{
    private readonly InventoryDbContext _dbContext;
    private readonly ImportPreviewOptions _options;
    private readonly IClock _clock;

    public EfImportPreviewBudget(
        InventoryDbContext dbContext,
        IOptions<ImportPreviewOptions> options,
        IClock clock)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _clock = clock;
    }

    public async Task<T> AdmitAsync<T>(
        Guid payloadId,
        ImportPreviewPayloadKind kind,
        Guid createdByUserId,
        int payloadBytes,
        DateTimeOffset createdAtUtc,
        Func<CancellationToken, Task<T>> persist,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persist);
        if (payloadId == Guid.Empty || createdByUserId == Guid.Empty ||
            payloadBytes < 0 || createdAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Import preview admission is invalid.");
        }

        var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            await LockGateAsync(cancellationToken);
            var now = _clock.UtcNow;
            var active = _dbContext.ImportPreviewReservations.AsNoTracking()
                .Where(reservation =>
                    reservation.PurgedAtUtc == null &&
                    reservation.PreservedAtUtc == null &&
                    reservation.ExpiresAtUtc > now);
            var user = active.Where(
                reservation => reservation.CreatedByUserId == createdByUserId);
            var userJobs = await user.LongCountAsync(
                reservation =>
                    reservation.Kind != ImportPreviewPayloadKind.LegacyReport,
                cancellationToken);
            var userBytes = await user.SumAsync(
                reservation => (long)reservation.PayloadBytes,
                cancellationToken);
            var overallBytes = await active.SumAsync(
                reservation => (long)reservation.PayloadBytes,
                cancellationToken);
            var newJob = kind == ImportPreviewPayloadKind.LegacyReport ? 0 : 1;
            bool OverBudget() =>
                userJobs + newJob > _options.MaximumActiveJobsPerUser ||
                userBytes + payloadBytes > _options.MaximumBytesPerUser ||
                overallBytes + payloadBytes > _options.MaximumBytesOverall;

            // Failed validations cannot be committed. When a user tries again,
            // recycle their oldest failed previews first so correction loops do
            // not exhaust the budget. Never evict a reconciliation report or its
            // source evidence, and never evict another user's preview.
            if (kind != ImportPreviewPayloadKind.LegacyReport && OverBudget())
            {
                var supersedable = await _dbContext.ImportPreviewReservations
                    .Where(reservation =>
                        reservation.CreatedByUserId == createdByUserId &&
                        reservation.PurgedAtUtc == null &&
                        reservation.PreservedAtUtc == null &&
                        reservation.ExpiresAtUtc > now &&
                        reservation.Kind != ImportPreviewPayloadKind.LegacyReport)
                    .OrderBy(reservation => reservation.CreatedAtUtc)
                    .ThenBy(reservation => reservation.PayloadId)
                    .ToListAsync(cancellationToken);
                foreach (var previous in supersedable)
                {
                    if (!OverBudget())
                    {
                        break;
                    }

                    var canSupersede = previous.Kind switch
                    {
                        ImportPreviewPayloadKind.Asset =>
                            await CanSupersedeAssetAsync(previous.PayloadId, cancellationToken),
                        ImportPreviewPayloadKind.ReferenceData =>
                            await CanSupersedeReferenceAsync(
                                previous.PayloadId, cancellationToken),
                        _ => false,
                    };
                    if (!canSupersede)
                    {
                        continue;
                    }

                    if (previous.Kind == ImportPreviewPayloadKind.Asset)
                    {
                        var job = await _dbContext.AssetImportJobs.FindAsync(
                            [previous.PayloadId], cancellationToken);
                        job!.PurgePreviewPayload();
                    }
                    else
                    {
                        var job = await _dbContext.ReferenceDataImportJobs.FindAsync(
                            [previous.PayloadId], cancellationToken);
                        job!.PurgePreviewPayload();
                    }

                    previous.MarkPurged(now);
                    userJobs--;
                    userBytes -= previous.PayloadBytes;
                    overallBytes -= previous.PayloadBytes;
                    _dbContext.AuditLogs.Add(AuditLog.Create(
                        actorUserId: null,
                        actorDisplayName: null,
                        action: "import.preview.superseded",
                        targetType: previous.Kind == ImportPreviewPayloadKind.Asset
                            ? "asset_import_job"
                            : "reference_data_import_job",
                        targetId: previous.PayloadId.ToString(),
                        occurredAtUtc: now,
                        outcome: "succeeded",
                        detailsJson: System.Text.Json.JsonSerializer.Serialize(new
                        {
                            previous.PayloadBytes,
                            previous.CreatedByUserId,
                            replacementPayloadId = payloadId,
                        })));
                }
            }

            if (OverBudget())
            {
                throw new ResourceConflictException(
                    "import_preview_budget_exceeded",
                    "The import preview storage limit is full. Commit or wait for " +
                    "older previews to expire before uploading another file.");
            }

            _dbContext.ImportPreviewReservations.Add(
                new ImportPreviewReservation(
                    payloadId,
                    kind,
                    createdByUserId,
                    payloadBytes,
                    createdAtUtc,
                    createdAtUtc.AddDays(_options.RetentionDays)));
            var result = await persist(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (Exception exception) when (IsSerializationConflict(exception))
        {
            throw new PersistenceConflictException(
                "import_preview_conflict",
                "Import previews changed concurrently. Retry the validation.",
                exception);
        }
    }

    public async Task EnsureAvailableAsync(
        Guid payloadId,
        ImportPreviewPayloadKind kind,
        CancellationToken cancellationToken)
    {
        var reservation = await _dbContext.ImportPreviewReservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PayloadId == payloadId && item.Kind == kind,
                cancellationToken);
        if (reservation is null || reservation.PurgedAtUtc is not null ||
            (reservation.PreservedAtUtc is null &&
             reservation.ExpiresAtUtc <= _clock.UtcNow))
        {
            throw PreviewExpired();
        }
    }

    public async Task<bool> IsWithinRetentionAsync(
        Guid payloadId,
        ImportPreviewPayloadKind kind,
        CancellationToken cancellationToken) =>
        await _dbContext.ImportPreviewReservations.AsNoTracking().AnyAsync(
            reservation => reservation.PayloadId == payloadId &&
                reservation.Kind == kind &&
                reservation.PurgedAtUtc == null &&
                reservation.ExpiresAtUtc > _clock.UtcNow,
            cancellationToken);

    public async Task ReleaseCommittedAsync(
        Guid payloadId,
        ImportPreviewPayloadKind kind,
        CancellationToken cancellationToken)
    {
        var reservation = await _dbContext.ImportPreviewReservations.SingleAsync(
            item => item.PayloadId == payloadId && item.Kind == kind,
            cancellationToken);
        if (reservation.PurgedAtUtc is not null || reservation.PreservedAtUtc is not null)
        {
            return;
        }

        var now = _clock.UtcNow;
        switch (kind)
        {
            case ImportPreviewPayloadKind.Asset:
                // A reconciliation run may still need the exact validated source rows.
                // Keep its normal retention and quota until sign-off preserves it;
                // preserving an unsigned run here would bypass expiry forever.
                if (await _dbContext.LegacyReconciliationRuns.AnyAsync(
                        run => run.AssetImportJobId == payloadId,
                        cancellationToken))
                {
                    return;
                }
                else
                {
                    var job = await _dbContext.AssetImportJobs.SingleAsync(
                        item => item.Id == payloadId, cancellationToken);
                    job.PurgePreviewPayload();
                    reservation.MarkPurged(now);
                }

                break;
            case ImportPreviewPayloadKind.ReferenceData:
                var referenceJob = await _dbContext.ReferenceDataImportJobs.SingleAsync(
                    item => item.Id == payloadId, cancellationToken);
                referenceJob.PurgePreviewPayload();
                reservation.MarkPurged(now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    public async Task PreserveLegacyEvidenceAsync(
        Guid runId,
        Guid assetImportJobId,
        CancellationToken cancellationToken)
    {
        await LockGateAsync(cancellationToken);
        var run = await _dbContext.ImportPreviewReservations.SingleOrDefaultAsync(
            item => item.PayloadId == runId &&
                item.Kind == ImportPreviewPayloadKind.LegacyReport,
            cancellationToken) ?? throw PreviewExpired();
        var job = await _dbContext.ImportPreviewReservations.SingleOrDefaultAsync(
            item => item.PayloadId == assetImportJobId &&
                item.Kind == ImportPreviewPayloadKind.Asset,
            cancellationToken) ?? throw PreviewExpired();
        var now = _clock.UtcNow;
        if (run.PurgedAtUtc is not null || job.PurgedAtUtc is not null ||
            run.ExpiresAtUtc <= now || job.ExpiresAtUtc <= now)
        {
            throw PreviewExpired();
        }

        run.Preserve(now);
        job.Preserve(now);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await LockGateAsync(cancellationToken);
        var now = _clock.UtcNow;
        var expired = await _dbContext.ImportPreviewReservations
            .Where(reservation =>
                reservation.ExpiresAtUtc <= now &&
                reservation.PurgedAtUtc == null &&
                reservation.PreservedAtUtc == null)
            .OrderBy(reservation => reservation.ExpiresAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var reservation in expired)
        {
            if (await IsSignedLegacyEvidenceAsync(reservation, cancellationToken))
            {
                reservation.Preserve(now);
                continue;
            }

            switch (reservation.Kind)
            {
                case ImportPreviewPayloadKind.Asset:
                    var assetJob = await _dbContext.AssetImportJobs.FindAsync(
                        [reservation.PayloadId], cancellationToken);
                    assetJob?.PurgePreviewPayload();
                    break;
                case ImportPreviewPayloadKind.ReferenceData:
                    var referenceJob = await _dbContext.ReferenceDataImportJobs.FindAsync(
                        [reservation.PayloadId], cancellationToken);
                    referenceJob?.PurgePreviewPayload();
                    break;
                case ImportPreviewPayloadKind.LegacyReport:
                    var run = await _dbContext.LegacyReconciliationRuns.FindAsync(
                        [reservation.PayloadId], cancellationToken);
                    run?.PurgeReportPayload();
                    break;
            }

            reservation.MarkPurged(now);
            _dbContext.AuditLogs.Add(AuditLog.Create(
                actorUserId: null,
                actorDisplayName: null,
                action: "import.preview.expired",
                targetType: reservation.Kind == ImportPreviewPayloadKind.LegacyReport
                    ? "legacy_reconciliation_run"
                    : reservation.Kind == ImportPreviewPayloadKind.Asset
                        ? "asset_import_job"
                        : "reference_data_import_job",
                targetId: reservation.PayloadId.ToString(),
                occurredAtUtc: now,
                outcome: "succeeded",
                detailsJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    reservation.PayloadBytes,
                    reservation.CreatedAtUtc,
                    reservation.CreatedByUserId,
                })));
        }

        await _dbContext.PendingMfaChallenges
            .Where(challenge => challenge.ExpiresAtUtc < now.AddDays(-1))
            .ExecuteDeleteAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return expired.Count;
    }

    private async Task<bool> IsSignedLegacyEvidenceAsync(
        ImportPreviewReservation reservation,
        CancellationToken cancellationToken)
    {
        if (reservation.Kind == ImportPreviewPayloadKind.LegacyReport)
        {
            return await _dbContext.LegacyReconciliationSignOffs.AnyAsync(
                signOff => _dbContext.LegacyReconciliationRuns.Any(
                    run => run.Id == signOff.ReconciliationRunId &&
                        run.Id == reservation.PayloadId),
                cancellationToken);
        }

        return reservation.Kind == ImportPreviewPayloadKind.Asset &&
            await _dbContext.LegacyReconciliationRuns.AnyAsync(
                run => run.AssetImportJobId == reservation.PayloadId &&
                    _dbContext.LegacyReconciliationSignOffs.Any(
                        signOff => signOff.ReconciliationRunId == run.Id),
                cancellationToken);
    }

    private async Task<bool> CanSupersedeAssetAsync(
        Guid payloadId,
        CancellationToken cancellationToken)
    {
        var job = await _dbContext.AssetImportJobs.FindAsync(
            [payloadId], cancellationToken);
        return job?.Status == Inventory.Domain.Importing.AssetImportStatus.ValidationFailed &&
            !await _dbContext.LegacyReconciliationRuns.AnyAsync(
                run => run.AssetImportJobId == payloadId,
                cancellationToken);
    }

    private async Task<bool> CanSupersedeReferenceAsync(
        Guid payloadId,
        CancellationToken cancellationToken)
    {
        var job = await _dbContext.ReferenceDataImportJobs.FindAsync(
            [payloadId], cancellationToken);
        return job?.Status ==
            Inventory.Domain.Importing.ReferenceDataImportStatus.ValidationFailed;
    }

    private async Task LockGateAsync(CancellationToken cancellationToken)
    {
        var affected = await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE \"ImportPreviewBudgetGates\" SET \"Revision\" = \"Revision\" + 1 WHERE \"Id\" = 1",
            cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                "The import preview budget gate is missing.");
        }
    }

    private static ResourceConflictException PreviewExpired() =>
        new(
            "import_preview_expired",
            "This import preview has expired. Upload and validate the file again.");

    private static bool IsSerializationConflict(Exception exception) =>
        exception.GetBaseException() is PostgresException
        {
            SqlState: PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected,
        } or SqliteException
        {
            SqliteErrorCode: 5 or 6,
        };
}
