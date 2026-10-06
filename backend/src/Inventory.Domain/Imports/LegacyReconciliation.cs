using Inventory.Domain.Common;

namespace Inventory.Domain.Importing;

public enum LegacyReconciliationDecision
{
    Approved,
    Rejected,
}

public sealed class LegacyReconciliationRun
{
    private const int MaximumReportLength = 4_000_000;

    private LegacyReconciliationRun()
    {
    }

    private LegacyReconciliationRun(
        Guid id,
        Guid assetImportJobId,
        string reportFingerprint,
        string reportJson,
        long targetAssetCount,
        DateTimeOffset createdAtUtc,
        Guid createdByUserId,
        string createdByDisplayName)
    {
        if (assetImportJobId == Guid.Empty ||
            createdByUserId == Guid.Empty ||
            targetAssetCount < 0)
        {
            throw new DomainRuleException(
                "invalid_reconciliation_run",
                "A reconciliation run requires an import, actor, and valid target count.");
        }

        ValidateUtc(createdAtUtc, nameof(CreatedAtUtc));
        Id = id;
        AssetImportJobId = assetImportJobId;
        ReportFingerprint = ValidateHash(
            reportFingerprint,
            nameof(ReportFingerprint));
        ReportJson = ValidateJson(
            reportJson,
            nameof(ReportJson),
            MaximumReportLength);
        TargetAssetCount = targetAssetCount;
        CreatedAtUtc = createdAtUtc;
        CreatedByUserId = createdByUserId;
        CreatedByDisplayName = TextRules.Required(
            createdByDisplayName,
            nameof(CreatedByDisplayName),
            200);
    }

    public Guid Id { get; private set; }

    public Guid AssetImportJobId { get; private set; }

    public string ReportFingerprint { get; private set; } = string.Empty;

    public string ReportJson { get; private set; } = "{}";

    public long TargetAssetCount { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public string CreatedByDisplayName { get; private set; } = string.Empty;

    public void PurgeReportPayload() => ReportJson = "{}";

    public static LegacyReconciliationRun Create(
        Guid assetImportJobId,
        string reportFingerprint,
        string reportJson,
        long targetAssetCount,
        DateTimeOffset createdAtUtc,
        Guid createdByUserId,
        string createdByDisplayName) =>
        new(
            Guid.NewGuid(),
            assetImportJobId,
            reportFingerprint,
            reportJson,
            targetAssetCount,
            createdAtUtc,
            createdByUserId,
            createdByDisplayName);

    private static string ValidateHash(string value, string fieldName)
    {
        var normalized = TextRules.Required(value, fieldName, 64)
            .ToUpperInvariant();
        if (normalized.Length != 64 ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new DomainRuleException(
                "invalid_reconciliation_hash",
                $"{fieldName} must be a SHA-256 hash.");
        }

        return normalized;
    }

    private static string ValidateJson(
        string value,
        string fieldName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength)
        {
            throw new DomainRuleException(
                "invalid_reconciliation_evidence",
                $"{fieldName} is missing or exceeds the supported size.");
        }

        return value;
    }

    private static void ValidateUtc(
        DateTimeOffset value,
        string fieldName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                $"{fieldName} must be UTC.");
        }
    }
}

public sealed class LegacyReconciliationSignOff
{
    private const int MaximumExceptionsLength = 1_000_000;

    private LegacyReconciliationSignOff()
    {
    }

    private LegacyReconciliationSignOff(
        Guid id,
        Guid reconciliationRunId,
        LegacyReconciliationDecision decision,
        string reportFingerprint,
        string signedExceptionsJson,
        string notes,
        DateTimeOffset signedAtUtc,
        Guid signedByUserId,
        string signedByDisplayName)
    {
        if (reconciliationRunId == Guid.Empty ||
            signedByUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "invalid_reconciliation_signoff",
                "A reconciliation sign-off requires a run and actor.");
        }

        if (signedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                "SignedAtUtc must be UTC.");
        }

        Id = id;
        ReconciliationRunId = reconciliationRunId;
        Decision = decision;
        ReportFingerprint = LegacyReconciliationRunHash(reportFingerprint);
        SignedExceptionsJson = ValidateExceptions(signedExceptionsJson);
        Notes = TextRules.Required(notes, nameof(Notes), 4000);
        SignedAtUtc = signedAtUtc;
        SignedByUserId = signedByUserId;
        SignedByDisplayName = TextRules.Required(
            signedByDisplayName,
            nameof(SignedByDisplayName),
            200);
    }

    public Guid Id { get; private set; }

    public Guid ReconciliationRunId { get; private set; }

    public LegacyReconciliationDecision Decision { get; private set; }

    public string ReportFingerprint { get; private set; } = string.Empty;

    public string SignedExceptionsJson { get; private set; } = "[]";

    public string Notes { get; private set; } = string.Empty;

    public DateTimeOffset SignedAtUtc { get; private set; }

    public Guid SignedByUserId { get; private set; }

    public string SignedByDisplayName { get; private set; } = string.Empty;

    public static LegacyReconciliationSignOff Create(
        Guid reconciliationRunId,
        LegacyReconciliationDecision decision,
        string reportFingerprint,
        string signedExceptionsJson,
        string notes,
        DateTimeOffset signedAtUtc,
        Guid signedByUserId,
        string signedByDisplayName) =>
        new(
            Guid.NewGuid(),
            reconciliationRunId,
            decision,
            reportFingerprint,
            signedExceptionsJson,
            notes,
            signedAtUtc,
            signedByUserId,
            signedByDisplayName);

    private static string LegacyReconciliationRunHash(string value)
    {
        var normalized = TextRules.Required(
                value,
                nameof(ReportFingerprint),
                64)
            .ToUpperInvariant();
        if (normalized.Length != 64 ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new DomainRuleException(
                "invalid_reconciliation_hash",
                "ReportFingerprint must be a SHA-256 hash.");
        }

        return normalized;
    }

    private static string ValidateExceptions(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaximumExceptionsLength)
        {
            throw new DomainRuleException(
                "invalid_reconciliation_exceptions",
                "SignedExceptionsJson is missing or exceeds the supported size.");
        }

        return value;
    }
}
