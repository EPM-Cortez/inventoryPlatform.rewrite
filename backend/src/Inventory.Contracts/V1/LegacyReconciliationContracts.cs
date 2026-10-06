using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public enum LegacyReconciliationDecisionContract
{
    Approved,
    Rejected,
}

public sealed class LegacyExceptionAcknowledgementRequest
{
    [Required]
    [RegularExpression("^[A-Fa-f0-9]{64}$")]
    public string IssueKey { get; init; } = string.Empty;

    [Required]
    [StringLength(1000)]
    public string Rationale { get; init; } = string.Empty;
}

public sealed class SignOffLegacyReconciliationRequest
{
    public LegacyReconciliationDecisionContract Decision { get; init; }

    [Required]
    [RegularExpression("^[A-Fa-f0-9]{64}$")]
    public string ReportFingerprint { get; init; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Notes { get; init; } = string.Empty;

    public IReadOnlyList<LegacyExceptionAcknowledgementRequest> Exceptions
    {
        get;
        init;
    } = [];
}

public sealed record LegacyReconciliationCountsResponse(
    int SourceRows,
    int ValidRows,
    int ErrorRows,
    int PlannedCreates,
    int PlannedUpdates,
    long TargetAssetsBefore,
    long ExpectedTargetAssetsAfter);

public sealed record LegacyReconciliationMappingSummaryResponse(
    int GeneratedAssetTags,
    int PlannedCategoryCreates,
    int PlannedLocationCreates,
    int InferredDateRows,
    int FuzzyPersonMatches,
    int UnassignedImports);

public sealed record LegacyReconciliationIssueResponse(
    string IssueKey,
    int RowNumber,
    string Column,
    string Code,
    string Classification,
    string Severity,
    string Message,
    string? Value);

public sealed record LegacyFieldComparisonResponse(
    string Field,
    string? Source,
    string? Target,
    bool Matches);

public sealed record LegacyReconciliationSampleResponse(
    int RowNumber,
    AssetImportActionContract Action,
    string AssetTag,
    bool TargetExists,
    string Outcome,
    IReadOnlyList<LegacyFieldComparisonResponse> Comparisons);

public sealed record LegacyReconciliationReportResponse(
    int FormatVersion,
    Guid ImportJobId,
    Guid ImportJobVersion,
    string SourceFileName,
    string SourceFileHash,
    AssetImportModeContract Mode,
    AssetImportStatusContract ImportStatus,
    DateTimeOffset CreatedAtUtc,
    LegacyReconciliationCountsResponse Counts,
    LegacyReconciliationMappingSummaryResponse Mappings,
    IReadOnlyList<LegacyReconciliationIssueResponse> Issues,
    int DuplicateIssueCount,
    int UnmappedReferenceIssueCount,
    IReadOnlyList<LegacyReconciliationSampleResponse> Samples,
    bool SamplesTruncated,
    string ReportFingerprint);

public sealed record LegacySignedIssueAcknowledgementResponse(
    string IssueKey,
    int RowNumber,
    string Column,
    string Code,
    string Rationale);

public sealed record LegacyReconciliationSignOffResponse(
    Guid Id,
    LegacyReconciliationDecisionContract Decision,
    string ReportFingerprint,
    IReadOnlyList<LegacySignedIssueAcknowledgementResponse> SignedExceptions,
    string Notes,
    DateTimeOffset SignedAtUtc,
    Guid SignedByUserId,
    string SignedByDisplayName);

public sealed record LegacyReconciliationResponse(
    LegacyReconciliationReportResponse Report,
    LegacyReconciliationSignOffResponse? SignOff,
    bool CutoverReady);

public sealed record LegacyCutoverPackageResponse(
    int FormatVersion,
    Guid ImportJobId,
    Guid ImportJobVersion,
    string SourceFileName,
    string SourceFileHash,
    string ReportFingerprint,
    string PackageFingerprint,
    AssetImportModeContract Mode,
    AssetImportStatusContract ImportStatus,
    LegacyReconciliationCountsResponse Counts,
    LegacyReconciliationSignOffResponse? SignOff,
    bool CutoverReady,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> CutoverProcedure,
    IReadOnlyList<string> RollbackProcedure);
