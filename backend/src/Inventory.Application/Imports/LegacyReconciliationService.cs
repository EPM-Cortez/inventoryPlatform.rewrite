using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Domain.Auditing;
using Inventory.Domain.Common;
using Inventory.Domain.Importing;

namespace Inventory.Application.DataExchange;

public sealed class LegacyReconciliationService
{
    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    private static readonly string[] CutoverProcedure =
    [
        "Create and verify a fresh online backup and matching data-protection key-ring backup.",
        "Stop competing import activity and confirm the signed report fingerprint.",
        "Commit the exact versioned import job through the supported import commit endpoint.",
        "Compare committed create, update, and unchanged counts with the signed package.",
        "Sample the recorded assets, history, audit, assignments, warranties, and active QR resolution.",
        "Record operator, release, UTC completion time, and health status before reopening normal writes.",
    ];

    private static readonly string[] RollbackProcedure =
    [
        "Stop the API and prevent further writes.",
        "Preserve the failed post-cutover database and logs for investigation.",
        "Verify the pre-cutover backup manifest and SHA-256 file evidence.",
        "Restore inventory.db and its matching data-protection key ring to the configured durable paths.",
        "Start one compatible API instance and confirm integrity, migrations, login, asset history, audit, and QR checks.",
        "Document the failed reconciliation fingerprint and require a new dry run before another cutover.",
    ];

    private readonly ImportExportService _importExportService;
    private readonly ILegacyReconciliationRepository _repository;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;
    private readonly LegacyReconciliationSettings _settings;
    private readonly IImportPreviewBudget _previewBudget;

    public LegacyReconciliationService(
        ImportExportService importExportService,
        ILegacyReconciliationRepository repository,
        IClock clock,
        ICurrentUserContext currentUser,
        LegacyReconciliationSettings settings,
        IImportPreviewBudget previewBudget)
    {
        _importExportService = importExportService;
        _repository = repository;
        _clock = clock;
        _currentUser = currentUser;
        _settings = settings;
        _previewBudget = previewBudget;
    }

    public async Task<LegacyReconciliationView> CreateDryRunAsync(
        AssetImportFile file,
        AssetImportMode mode,
        CancellationToken cancellationToken)
    {
        var actor = _currentUser.RequireActor();
        await using var transaction =
            await _repository.BeginTransactionAsync(cancellationToken);
        var job = await _importExportService.ValidateAssetImportAsync(
            file,
            mode,
            cancellationToken);
        var snapshot =
            await _importExportService.GetLegacyReconciliationSnapshotAsync(
                job.Id,
                cancellationToken);
        var targetCount = await _repository.CountAssetsAsync(
            cancellationToken);
        var report = await BuildReportAsync(
            snapshot,
            targetCount,
            cancellationToken);
        var reportJson = JsonSerializer.Serialize(report, JsonOptions);

        LegacyReconciliationRun run;
        try
        {
            run = LegacyReconciliationRun.Create(
                job.Id,
                report.ReportFingerprint,
                reportJson,
                targetCount,
                _clock.UtcNow,
                actor.UserId,
                actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw new RequestValidationException(
                exception.Code,
                exception.Message);
        }

        await _previewBudget.AdmitAsync(
            run.Id,
            ImportPreviewPayloadKind.LegacyReport,
            actor.UserId,
            Encoding.UTF8.GetByteCount(reportJson),
            run.CreatedAtUtc,
            async token =>
            {
                _repository.AddRun(run);
                _repository.AddAuditLog(
                    CreateAudit(
                        actor,
                        "legacy.reconciliation.dry_run",
                        run.Id,
                        new
                        {
                            importJobId = job.Id,
                            report.ReportFingerprint,
                            report.SourceFileHash,
                            report.Mode,
                            report.ImportStatus,
                            report.Counts,
                            report.DuplicateIssueCount,
                            report.UnmappedReferenceIssueCount,
                        }));
                await _repository.SaveChangesAsync(token);
                return true;
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new LegacyReconciliationView(
            report,
            SignOff: null,
            CutoverReady: false);
    }

    public async Task<LegacyReconciliationView> GetAsync(
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        var (run, report, signOff) = await GetEvidenceAsync(
            importJobId,
            cancellationToken);
        var currentJob = await _importExportService.GetImportJobAsync(
            importJobId,
            cancellationToken);
        var previewCanCommit = currentJob.Status == AssetImportStatus.Committed ||
            await _previewBudget.IsWithinRetentionAsync(
                importJobId,
                ImportPreviewPayloadKind.Asset,
                cancellationToken);
        return new LegacyReconciliationView(
            report,
            signOff,
            previewCanCommit && IsCutoverReady(report, signOff));
    }

    public async Task<LegacyReconciliationView> SignOffAsync(
        Guid importJobId,
        LegacyReconciliationSignOffCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actor = _currentUser.RequireActor();
        var run = await _repository.GetRunByImportJobAsync(
            importJobId,
            cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Legacy reconciliation run",
                importJobId);
        await _previewBudget.EnsureAvailableAsync(
            run.Id,
            ImportPreviewPayloadKind.LegacyReport,
            cancellationToken);
        if (await _repository.GetSignOffAsync(
                run.Id,
                cancellationToken) is not null)
        {
            throw new ResourceConflictException(
                "reconciliation_already_signed",
                "This reconciliation run already has an immutable sign-off. " +
                "Create a new dry run for a new decision.");
        }

        var report = DeserializeReport(run);
        if (!HashesEqual(
                report.ReportFingerprint,
                command.ReportFingerprint))
        {
            throw new ResourceConflictException(
                "reconciliation_report_changed",
                "The supplied report fingerprint does not match the persisted dry run.");
        }

        var signedExceptions = ValidateSignedExceptions(
            report,
            command);
        var notes = RequireText(
            command.Notes,
            "notes",
            4000);
        var signedExceptionsJson = JsonSerializer.Serialize(
            signedExceptions,
            JsonOptions);

        LegacyReconciliationSignOff signOff;
        try
        {
            signOff = LegacyReconciliationSignOff.Create(
                run.Id,
                command.Decision,
                report.ReportFingerprint,
                signedExceptionsJson,
                notes,
                _clock.UtcNow,
                actor.UserId,
                actor.DisplayName);
        }
        catch (DomainRuleException exception)
        {
            throw new RequestValidationException(
                exception.Code,
                exception.Message);
        }

        var signOffView = ToView(signOff, signedExceptions);
        var cutoverReady = IsCutoverReady(report, signOffView);
        await using var transaction =
            await _repository.BeginTransactionAsync(cancellationToken);
        await _previewBudget.PreserveLegacyEvidenceAsync(
            run.Id,
            run.AssetImportJobId,
            cancellationToken);
        _repository.AddSignOff(signOff);
        _repository.AddAuditLog(
            CreateAudit(
                actor,
                "legacy.reconciliation.signed",
                run.Id,
                new
                {
                    importJobId,
                    command.Decision,
                    report.ReportFingerprint,
                    signedExceptionCount = signedExceptions.Length,
                    cutoverReady,
                }));
        await _repository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new LegacyReconciliationView(
            report,
            signOffView,
            cutoverReady);
    }

    public async Task<LegacyCutoverPackage> GetCutoverPackageAsync(
        Guid importJobId,
        CancellationToken cancellationToken)
    {
        var (_, report, signOff) = await GetEvidenceAsync(
            importJobId,
            cancellationToken);
        var currentJob = await _importExportService.GetImportJobAsync(
            importJobId,
            cancellationToken);
        var blockingReasons = BuildBlockingReasons(
            report,
            currentJob,
            signOff);
        if (currentJob.Status != AssetImportStatus.Committed &&
            !await _previewBudget.IsWithinRetentionAsync(
                importJobId,
                ImportPreviewPayloadKind.Asset,
                cancellationToken))
        {
            blockingReasons.Add(
                "The import preview has expired and cannot be committed. " +
                "Upload and validate the file again.");
        }
        var cutoverReady = blockingReasons.Count == 0;
        var packageEvidence = new
        {
            formatVersion = 1,
            report.ImportJobId,
            currentJob.Version,
            report.SourceFileName,
            report.SourceFileHash,
            report.ReportFingerprint,
            report.Mode,
            importStatus = currentJob.Status,
            report.Counts,
            signOff,
            cutoverReady,
            blockingReasons,
            cutoverProcedure = CutoverProcedure,
            rollbackProcedure = RollbackProcedure,
        };
        var packageFingerprint = Hash(
            JsonSerializer.SerializeToUtf8Bytes(
                packageEvidence,
                JsonOptions));
        return new LegacyCutoverPackage(
            FormatVersion: 1,
            ImportJobId: report.ImportJobId,
            ImportJobVersion: currentJob.Version,
            SourceFileName: report.SourceFileName,
            SourceFileHash: report.SourceFileHash,
            ReportFingerprint: report.ReportFingerprint,
            PackageFingerprint: packageFingerprint,
            Mode: report.Mode,
            ImportStatus: currentJob.Status,
            Counts: report.Counts,
            SignOff: signOff,
            CutoverReady: cutoverReady,
            BlockingReasons: blockingReasons,
            CutoverProcedure: CutoverProcedure,
            RollbackProcedure: RollbackProcedure);
    }

    private async Task<LegacyReconciliationReport> BuildReportAsync(
        LegacyImportSnapshot snapshot,
        long targetCount,
        CancellationToken cancellationToken)
    {
        var sampleRows = snapshot.Rows
            .OrderBy(row => row.RowNumber)
            .ThenBy(row => row.AssetTag, StringComparer.Ordinal)
            .Take(_settings.SampleRows)
            .ToArray();
        var targetIds = sampleRows
            .Where(row => row.TargetAssetId.HasValue)
            .Select(row => row.TargetAssetId!.Value)
            .Distinct()
            .ToArray();
        var targets = (
            await _repository.GetTargetAssetsAsync(
                targetIds,
                cancellationToken))
            .ToDictionary(target => target.Id);
        var issues = BuildIssues(snapshot);
        foreach (var row in sampleRows.Where(
            row =>
                row.Action == AssetImportAction.Update &&
                row.TargetAssetId.HasValue &&
                !targets.ContainsKey(row.TargetAssetId.Value)))
        {
            issues.Add(
                CreateIssue(
                    row.RowNumber,
                    "assetTag",
                    "target_asset_missing",
                    "dataQuality",
                    "blocking",
                    "The update target was not available for sampled comparison.",
                    row.AssetTag));
        }

        var samples = sampleRows
            .Select(
                row =>
                {
                    LegacyTargetAssetSnapshot? target = null;
                    if (row.TargetAssetId.HasValue)
                    {
                        targets.TryGetValue(
                            row.TargetAssetId.Value,
                            out target);
                    }

                    return CreateSample(row, target);
                })
            .ToArray();
        var orderedIssues = issues
            .OrderBy(issue => issue.RowNumber)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.IssueKey, StringComparer.Ordinal)
            .ToArray();
        var counts = new LegacyReconciliationCounts(
            SourceRows: snapshot.Job.TotalRows,
            ValidRows: snapshot.Job.ValidRows,
            ErrorRows: snapshot.Job.ErrorRows,
            PlannedCreates: snapshot.Job.CreateRows,
            PlannedUpdates: snapshot.Job.UpdateRows,
            TargetAssetsBefore: targetCount,
            ExpectedTargetAssetsAfter:
                checked(targetCount + snapshot.Job.CreateRows));
        var mappings = new LegacyReconciliationMappingSummary(
            snapshot.Job.GeneratedAssetTagRows,
            snapshot.Job.PlannedCategoryCreateCount,
            snapshot.Job.PlannedLocationCreateCount,
            snapshot.Job.InferredDateRows,
            snapshot.Job.FuzzyPersonMatchRows,
            snapshot.Job.UnassignedImportRows);
        var duplicateCount = orderedIssues.Count(
            issue => issue.Classification == "duplicate");
        var unmappedCount = orderedIssues.Count(
            issue => issue.Classification == "unmappedReference");
        var evidence = new
        {
            formatVersion = 1,
            snapshot.Job.SchemaVersion,
            snapshot.Job.FileHash,
            snapshot.Job.Mode,
            snapshot.Job.Status,
            counts,
            mappings,
            issues = orderedIssues,
            samples,
            samplesTruncated =
                snapshot.Rows.Count > samples.Length,
        };
        var reportFingerprint = Hash(
            JsonSerializer.SerializeToUtf8Bytes(
                evidence,
                JsonOptions));
        return new LegacyReconciliationReport(
            FormatVersion: 1,
            ImportJobId: snapshot.Job.Id,
            ImportJobVersion: snapshot.Job.Version,
            SourceFileName: snapshot.Job.OriginalFileName,
            SourceFileHash: snapshot.Job.FileHash,
            Mode: snapshot.Job.Mode,
            ImportStatus: snapshot.Job.Status,
            CreatedAtUtc: snapshot.Job.CreatedAtUtc,
            Counts: counts,
            Mappings: mappings,
            Issues: orderedIssues,
            DuplicateIssueCount: duplicateCount,
            UnmappedReferenceIssueCount: unmappedCount,
            Samples: samples,
            SamplesTruncated:
                snapshot.Rows.Count > samples.Length,
            ReportFingerprint: reportFingerprint);
    }

    private static List<LegacyReconciliationIssue> BuildIssues(
        LegacyImportSnapshot snapshot)
    {
        var issues = snapshot.Errors
            .Select(
                error =>
                {
                    var classification =
                        ClassifyError(error);
                    return CreateIssue(
                        error.RowNumber,
                        error.Column,
                        error.Code,
                        classification,
                        "blocking",
                        error.Message,
                        error.Value);
                })
            .ToList();

        foreach (var row in snapshot.Rows)
        {
            if (row.CategoryWillBeCreated)
            {
                issues.Add(
                    CreateIssue(
                        row.RowNumber,
                        "category",
                        "planned_category_create",
                        "mappingDecision",
                        "information",
                        "The importer will create this missing category atomically.",
                        row.CategoryCode));
            }

            if (row.LocationWillBeCreated)
            {
                issues.Add(
                    CreateIssue(
                        row.RowNumber,
                        "locationCode",
                        "planned_location_create",
                        "mappingDecision",
                        "information",
                        "The importer will create this missing location atomically.",
                        row.LocationCode));
            }

            if (row.AssignmentResolution ==
                AssetImportAssignmentResolution.FuzzyPerson)
            {
                issues.Add(
                    CreateIssue(
                        row.RowNumber,
                        "assignedPersonExternalId",
                        "fuzzy_person_match",
                        "mappingDecision",
                        "warning",
                        "The source person identifier used the bounded fuzzy-match rule.",
                        row.AssignmentSource));
            }
            else if (
                row.AssignmentResolution ==
                    AssetImportAssignmentResolution.Unassigned &&
                !string.IsNullOrWhiteSpace(row.AssignmentSource))
            {
                issues.Add(
                    CreateIssue(
                        row.RowNumber,
                        "assignment",
                        "unmapped_assignment_imported_unassigned",
                        "unmappedReference",
                        "warning",
                        "The source person or department was not resolved; the asset will import unassigned.",
                        row.AssignmentSource));
            }

            if (row.InferredDateFields.Count > 0)
            {
                issues.Add(
                    CreateIssue(
                        row.RowNumber,
                        "date",
                        "inferred_partial_date",
                        "dataQuality",
                        "information",
                        "One or more partial dates were normalised deterministically.",
                        string.Join(",", row.InferredDateFields)));
            }
        }

        return issues
            .DistinctBy(issue => issue.IssueKey)
            .ToList();
    }

    private static LegacyReconciliationSample CreateSample(
        LegacyImportSourceRow source,
        LegacyTargetAssetSnapshot? target)
    {
        if (source.Action == AssetImportAction.Create)
        {
            return new LegacyReconciliationSample(
                source.RowNumber,
                source.Action,
                source.AssetTag,
                TargetExists: false,
                Outcome: "plannedCreate",
                Comparisons: []);
        }

        if (target is null)
        {
            return new LegacyReconciliationSample(
                source.RowNumber,
                source.Action,
                source.AssetTag,
                TargetExists: false,
                Outcome: "targetMissing",
                Comparisons: []);
        }

        return new LegacyReconciliationSample(
            source.RowNumber,
            source.Action,
            source.AssetTag,
            TargetExists: true,
            Outcome: "compared",
            Comparisons:
            [
                Compare(
                    "assetTag",
                    source.AssetTag,
                    target.AssetTag,
                    ignoreCase: true),
                Compare(
                    "deviceName",
                    source.DeviceName,
                    target.DeviceName),
                Compare(
                    "serialNumber",
                    source.SerialNumber,
                    target.SerialNumber),
                Compare(
                    "categoryCode",
                    source.CategoryCode,
                    target.CategoryCode,
                    ignoreCase: true),
                Compare(
                    "status",
                    source.Status.ToString(),
                    target.Status.ToString()),
                Compare(
                    "locationCode",
                    source.LocationCode,
                    target.LocationCode,
                    ignoreCase: true),
                Compare(
                    "legacyId",
                    source.LegacyId,
                    target.LegacyId),
            ]);
    }

    private LegacySignedIssueAcknowledgement[]
        ValidateSignedExceptions(
            LegacyReconciliationReport report,
            LegacyReconciliationSignOffCommand command)
    {
        if (command.Exceptions.Count >
            _settings.MaximumSignedExceptions)
        {
            throw Validation(
                "exceptions",
                $"No more than {_settings.MaximumSignedExceptions} exceptions can be signed.");
        }

        var blockingIssues = report.Issues
            .Where(issue => issue.Severity == "blocking")
            .ToDictionary(
                issue => issue.IssueKey,
                StringComparer.Ordinal);
        var supplied = new Dictionary<
            string,
            LegacySignedIssueAcknowledgement>(StringComparer.Ordinal);
        foreach (var acknowledgement in command.Exceptions)
        {
            if (!blockingIssues.TryGetValue(
                    acknowledgement.IssueKey,
                    out var issue))
            {
                throw Validation(
                    "exceptions",
                    $"Issue key '{acknowledgement.IssueKey}' is not a blocking issue in this report.");
            }

            if (supplied.ContainsKey(issue.IssueKey))
            {
                throw Validation(
                    "exceptions",
                    $"Issue key '{issue.IssueKey}' was supplied more than once.");
            }

            var rationale = RequireText(
                acknowledgement.Rationale,
                "exceptions",
                1000);
            supplied.Add(
                issue.IssueKey,
                new LegacySignedIssueAcknowledgement(
                    issue.IssueKey,
                    issue.RowNumber,
                    issue.Column,
                    issue.Code,
                    rationale));
        }

        if (command.Decision ==
                LegacyReconciliationDecision.Approved &&
            blockingIssues.Keys.Any(key => !supplied.ContainsKey(key)))
        {
            throw Validation(
                "exceptions",
                "Approval requires a rationale for every blocking issue. " +
                "Acknowledgement records the exception but does not make a failed import committable.");
        }

        return supplied.Values
            .OrderBy(exception => exception.RowNumber)
            .ThenBy(exception => exception.Code, StringComparer.Ordinal)
            .ThenBy(exception => exception.IssueKey, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<(
        LegacyReconciliationRun Run,
        LegacyReconciliationReport Report,
        LegacyReconciliationSignOffView? SignOff)> GetEvidenceAsync(
            Guid importJobId,
            CancellationToken cancellationToken)
    {
        var run = await _repository.GetRunByImportJobAsync(
            importJobId,
            cancellationToken)
            ?? throw new ResourceNotFoundException(
                "Legacy reconciliation run",
                importJobId);
        await _previewBudget.EnsureAvailableAsync(
            run.Id,
            ImportPreviewPayloadKind.LegacyReport,
            cancellationToken);
        var report = DeserializeReport(run);
        var signOff = await _repository.GetSignOffAsync(
            run.Id,
            cancellationToken);
        return (
            run,
            report,
            signOff is null ? null : ToView(signOff));
    }

    private static LegacyReconciliationReport DeserializeReport(
        LegacyReconciliationRun run)
    {
        var report =
            JsonSerializer.Deserialize<LegacyReconciliationReport>(
                run.ReportJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The persisted reconciliation report is unreadable.");
        if (!HashesEqual(
                report.ReportFingerprint,
                run.ReportFingerprint))
        {
            throw new InvalidOperationException(
                "The persisted reconciliation report fingerprint does not match its record.");
        }

        return report;
    }

    private static LegacyReconciliationSignOffView ToView(
        LegacyReconciliationSignOff signOff)
    {
        var exceptions =
            JsonSerializer.Deserialize<
                IReadOnlyList<LegacySignedIssueAcknowledgement>>(
                signOff.SignedExceptionsJson,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "The persisted signed exceptions are unreadable.");
        return ToView(signOff, exceptions);
    }

    private static LegacyReconciliationSignOffView ToView(
        LegacyReconciliationSignOff signOff,
        IReadOnlyList<LegacySignedIssueAcknowledgement> exceptions) =>
        new(
            signOff.Id,
            signOff.Decision,
            signOff.ReportFingerprint,
            exceptions,
            signOff.Notes,
            signOff.SignedAtUtc,
            signOff.SignedByUserId,
            signOff.SignedByDisplayName);

    private static List<string> BuildBlockingReasons(
        LegacyReconciliationReport report,
        AssetImportJobView currentJob,
        LegacyReconciliationSignOffView? signOff)
    {
        var reasons = new List<string>();
        if (signOff is null)
        {
            reasons.Add(
                "The reconciliation report has not been signed.");
        }
        else if (
            signOff.Decision !=
            LegacyReconciliationDecision.Approved)
        {
            reasons.Add(
                "The reconciliation decision is rejected.");
        }

        if (report.Counts.ErrorRows > 0)
        {
            reasons.Add(
                "The import has validation errors; signed exceptions do not bypass the commit gate.");
        }

        if (currentJob.Status is not (
                AssetImportStatus.ReadyToCommit or
                AssetImportStatus.Committed))
        {
            reasons.Add(
                "The current import job is not ready to commit.");
        }

        if (currentJob.Version != report.ImportJobVersion &&
            currentJob.Status != AssetImportStatus.Committed)
        {
            reasons.Add(
                "The current import job version differs from the signed dry run.");
        }

        return reasons;
    }

    private static bool IsCutoverReady(
        LegacyReconciliationReport report,
        LegacyReconciliationSignOffView? signOff) =>
        signOff?.Decision ==
            LegacyReconciliationDecision.Approved &&
        report.ImportStatus == AssetImportStatus.ReadyToCommit &&
        report.Counts.ErrorRows == 0;

    private static LegacyFieldComparison Compare(
        string field,
        string? source,
        string? target,
        bool ignoreCase = false)
    {
        var normalizedSource = NormalizeComparison(source);
        var normalizedTarget = NormalizeComparison(target);
        return new LegacyFieldComparison(
            field,
            source,
            target,
            string.Equals(
                normalizedSource,
                normalizedTarget,
                ignoreCase
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal));
    }

    private static string? NormalizeComparison(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LegacyReconciliationIssue CreateIssue(
        int rowNumber,
        string column,
        string code,
        string classification,
        string severity,
        string message,
        string? value)
    {
        var issueEvidence = string.Join(
            "\u001f",
            rowNumber.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            column,
            code,
            value ?? string.Empty);
        return new LegacyReconciliationIssue(
            Hash(Encoding.UTF8.GetBytes(issueEvidence)),
            rowNumber,
            column,
            code,
            classification,
            severity,
            message,
            value);
    }

    private static string ClassifyError(
        AssetImportError error)
    {
        if (error.Code.Contains(
                "duplicate",
                StringComparison.Ordinal))
        {
            return "duplicate";
        }

        if (error.Code.Contains(
                "category",
                StringComparison.Ordinal) ||
            error.Code.Contains(
                "location",
                StringComparison.Ordinal) ||
            error.Code.Contains(
                "department",
                StringComparison.Ordinal) ||
            error.Code.Contains(
                "person",
                StringComparison.Ordinal) ||
            error.Column is
                "category" or
                "locationCode" or
                "departmentCode" or
                "assignedPersonExternalId")
        {
            return "unmappedReference";
        }

        return "dataQuality";
    }

    private AuditLog CreateAudit(
        CurrentActor actor,
        string action,
        Guid runId,
        object details) =>
        AuditLog.Create(
            actor.UserId,
            actor.DisplayName,
            action,
            "legacyReconciliationRun",
            runId.ToString(),
            _clock.UtcNow,
            "succeeded",
            JsonSerializer.Serialize(details, JsonOptions),
            actor.IpAddress,
            actor.Client,
            actor.CorrelationId);

    private static RequestValidationException Validation(
        string field,
        string message) =>
        new(
            "invalid_reconciliation_signoff",
            "The reconciliation sign-off is invalid.",
            new Dictionary<string, string[]>
            {
                [field] = [message],
            });

    private static string RequireText(
        string? value,
        string field,
        int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength)
        {
            throw Validation(
                field,
                $"{field} is required and cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    private static bool HashesEqual(
        string left,
        string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(
            JsonSerializerDefaults.Web);
        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));
        return options;
    }
}
