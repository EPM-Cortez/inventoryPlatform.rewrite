using Inventory.Domain.Assets;
using Inventory.Domain.Importing;

namespace Inventory.Application.DataExchange;

public sealed class LegacyReconciliationSettings
{
    public int SampleRows { get; init; } = 20;

    public int MaximumSignedExceptions { get; init; } = 1_000;

    public void Validate()
    {
        if (SampleRows is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "LegacyReconciliation:SampleRows must be between 1 and 100.");
        }

        if (MaximumSignedExceptions is < 1 or > 10_000)
        {
            throw new InvalidOperationException(
                "LegacyReconciliation:MaximumSignedExceptions must be between 1 and 10000.");
        }
    }
}

public sealed record LegacyReconciliationCounts(
    int SourceRows,
    int ValidRows,
    int ErrorRows,
    int PlannedCreates,
    int PlannedUpdates,
    long TargetAssetsBefore,
    long ExpectedTargetAssetsAfter);

public sealed record LegacyReconciliationMappingSummary(
    int GeneratedAssetTags,
    int PlannedCategoryCreates,
    int PlannedLocationCreates,
    int InferredDateRows,
    int FuzzyPersonMatches,
    int UnassignedImports);

public sealed record LegacyReconciliationIssue(
    string IssueKey,
    int RowNumber,
    string Column,
    string Code,
    string Classification,
    string Severity,
    string Message,
    string? Value);

public sealed record LegacyFieldComparison(
    string Field,
    string? Source,
    string? Target,
    bool Matches);

public sealed record LegacyReconciliationSample(
    int RowNumber,
    AssetImportAction Action,
    string AssetTag,
    bool TargetExists,
    string Outcome,
    IReadOnlyList<LegacyFieldComparison> Comparisons);

public sealed record LegacyReconciliationReport(
    int FormatVersion,
    Guid ImportJobId,
    Guid ImportJobVersion,
    string SourceFileName,
    string SourceFileHash,
    AssetImportMode Mode,
    AssetImportStatus ImportStatus,
    DateTimeOffset CreatedAtUtc,
    LegacyReconciliationCounts Counts,
    LegacyReconciliationMappingSummary Mappings,
    IReadOnlyList<LegacyReconciliationIssue> Issues,
    int DuplicateIssueCount,
    int UnmappedReferenceIssueCount,
    IReadOnlyList<LegacyReconciliationSample> Samples,
    bool SamplesTruncated,
    string ReportFingerprint);

public sealed record LegacySignedIssueAcknowledgement(
    string IssueKey,
    int RowNumber,
    string Column,
    string Code,
    string Rationale);

public sealed record LegacyExceptionAcknowledgement(
    string IssueKey,
    string Rationale);

public sealed record LegacyReconciliationSignOffCommand(
    LegacyReconciliationDecision Decision,
    string ReportFingerprint,
    string Notes,
    IReadOnlyList<LegacyExceptionAcknowledgement> Exceptions);

public sealed record LegacyReconciliationSignOffView(
    Guid Id,
    LegacyReconciliationDecision Decision,
    string ReportFingerprint,
    IReadOnlyList<LegacySignedIssueAcknowledgement> SignedExceptions,
    string Notes,
    DateTimeOffset SignedAtUtc,
    Guid SignedByUserId,
    string SignedByDisplayName);

public sealed record LegacyReconciliationView(
    LegacyReconciliationReport Report,
    LegacyReconciliationSignOffView? SignOff,
    bool CutoverReady);

public sealed record LegacyCutoverPackage(
    int FormatVersion,
    Guid ImportJobId,
    Guid ImportJobVersion,
    string SourceFileName,
    string SourceFileHash,
    string ReportFingerprint,
    string PackageFingerprint,
    AssetImportMode Mode,
    AssetImportStatus ImportStatus,
    LegacyReconciliationCounts Counts,
    LegacyReconciliationSignOffView? SignOff,
    bool CutoverReady,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> CutoverProcedure,
    IReadOnlyList<string> RollbackProcedure);

internal sealed record LegacyImportSourceRow(
    int RowNumber,
    AssetImportAction Action,
    Guid? TargetAssetId,
    string AssetTag,
    string? DeviceName,
    string? SerialNumber,
    string CategoryCode,
    bool CategoryWillBeCreated,
    AssetStatus Status,
    string? LocationCode,
    bool LocationWillBeCreated,
    string? AssignmentSource,
    AssetImportAssignmentResolution AssignmentResolution,
    string? AssignedTo,
    string? LegacyId,
    IReadOnlyList<string> InferredDateFields);

internal sealed record LegacyImportSnapshot(
    AssetImportJobView Job,
    IReadOnlyList<LegacyImportSourceRow> Rows,
    IReadOnlyList<AssetImportError> Errors);

public sealed record LegacyTargetAssetSnapshot(
    Guid Id,
    string AssetTag,
    string? DeviceName,
    string? SerialNumber,
    string CategoryCode,
    AssetStatus Status,
    string? LocationCode,
    string? LegacyId);
