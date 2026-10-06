using Inventory.Domain.Common;

namespace Inventory.Domain.Warranties;

public enum WarrantyState
{
    NotRecorded,
    Future,
    Active,
    ExpiringSoon,
    Expired,
}

public sealed class WarrantyCoverage
{
    private WarrantyCoverage()
    {
    }

    private WarrantyCoverage(
        Guid id,
        Guid assetId,
        string provider,
        string coverageType,
        DateOnly? startDate,
        DateOnly? endDate,
        string? reference,
        string? notes,
        bool isPrimary,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        AssetId = assetId;
        CreatedAtUtc = ValidateUtc(createdAtUtc);
        UpdateCore(
            provider,
            coverageType,
            startDate,
            endDate,
            reference,
            notes,
            isPrimary,
            createdAtUtc);
    }

    public Guid Id { get; private set; }

    public Guid AssetId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string CoverageType { get; private set; } = string.Empty;

    public DateOnly? StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public string? Reference { get; private set; }

    public string? Notes { get; private set; }

    public bool IsPrimary { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public Guid Version { get; private set; }

    public bool IsArchived => ArchivedAtUtc.HasValue;

    public static WarrantyCoverage Create(
        Guid assetId,
        string provider,
        string coverageType,
        DateOnly? startDate,
        DateOnly? endDate,
        string? reference,
        string? notes,
        bool isPrimary,
        DateTimeOffset createdAtUtc)
    {
        if (assetId == Guid.Empty)
        {
            throw new DomainRuleException("invalid_asset", "Warranty asset ID is required.");
        }

        return new WarrantyCoverage(
            Guid.NewGuid(),
            assetId,
            provider,
            coverageType,
            startDate,
            endDate,
            reference,
            notes,
            isPrimary,
            createdAtUtc);
    }

    public void Update(
        string provider,
        string coverageType,
        DateOnly? startDate,
        DateOnly? endDate,
        string? reference,
        string? notes,
        bool isPrimary,
        Guid expectedVersion,
        DateTimeOffset updatedAtUtc)
    {
        EnsureVersion(expectedVersion);
        if (IsArchived)
        {
            throw new DomainRuleException(
                "warranty_archived",
                "An archived warranty cannot be changed.");
        }

        UpdateCore(
            provider,
            coverageType,
            startDate,
            endDate,
            reference,
            notes,
            isPrimary,
            updatedAtUtc);
    }

    public void Archive(Guid expectedVersion, DateTimeOffset archivedAtUtc)
    {
        EnsureVersion(expectedVersion);
        if (IsArchived)
        {
            throw new DomainRuleException(
                "warranty_already_archived",
                "The warranty is already archived.");
        }

        ArchivedAtUtc = ValidateUtc(archivedAtUtc);
        IsPrimary = false;
        UpdatedAtUtc = archivedAtUtc;
        Version = Guid.NewGuid();
    }

    public WarrantyState CalculateState(DateOnly today, int warningDays)
    {
        if (warningDays is < 1 or > 365)
        {
            throw new ArgumentOutOfRangeException(
                nameof(warningDays),
                "Warning days must be between 1 and 365.");
        }

        if (!StartDate.HasValue && !EndDate.HasValue)
        {
            return WarrantyState.NotRecorded;
        }

        if (StartDate.HasValue && StartDate.Value > today)
        {
            return WarrantyState.Future;
        }

        if (EndDate.HasValue && EndDate.Value < today)
        {
            return WarrantyState.Expired;
        }

        if (EndDate.HasValue && EndDate.Value <= today.AddDays(warningDays))
        {
            return WarrantyState.ExpiringSoon;
        }

        return WarrantyState.Active;
    }

    private void UpdateCore(
        string provider,
        string coverageType,
        DateOnly? startDate,
        DateOnly? endDate,
        string? reference,
        string? notes,
        bool isPrimary,
        DateTimeOffset updatedAtUtc)
    {
        if (startDate.HasValue &&
            endDate.HasValue &&
            endDate.Value < startDate.Value)
        {
            throw new DomainRuleException(
                "invalid_warranty_dates",
                "Warranty end date cannot precede its start date.");
        }

        Provider = TextRules.Required(provider, nameof(Provider), 200);
        CoverageType = TextRules.Required(coverageType, nameof(CoverageType), 100);
        StartDate = startDate;
        EndDate = endDate;
        Reference = TextRules.Optional(reference, nameof(Reference), 200);
        Notes = TextRules.Optional(notes, nameof(Notes), 2000);
        IsPrimary = isPrimary;
        UpdatedAtUtc = ValidateUtc(updatedAtUtc);
        Version = Guid.NewGuid();
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || expectedVersion != Version)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The warranty was changed by another user.");
        }
    }

    private static DateTimeOffset ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                "Warranty timestamps must be UTC.");
        }

        return value;
    }
}
