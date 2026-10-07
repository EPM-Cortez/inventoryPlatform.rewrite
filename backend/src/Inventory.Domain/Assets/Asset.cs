using Inventory.Domain.Common;

namespace Inventory.Domain.Assets;

public sealed record AssetDetails(
    string AssetTag,
    Guid CategoryId,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    AssetStatus Status,
    AssetCondition Condition,
    Guid? LocationId,
    DateOnly? PurchaseDate,
    decimal? PurchaseCost,
    string? Currency,
    string? Supplier,
    string? Notes,
    string? LegacyId,
    string? DeviceName = null,
    string? Description = null,
    string? LanMacAddress = null,
    Guid? BiosGuid = null,
    string? Ownership = null,
    string? OperatingSystem = null,
    string? OperatingSystemVersion = null,
    bool? Windows11Compliant = null);

public sealed record FieldChange(object? From, object? To);

public sealed class Asset
{
    private const decimal MaximumPurchaseCost = 9999999999999999.99m;

    private Asset()
    {
    }

    private Asset(Guid id, AssetDetails details, DateTimeOffset createdAtUtc)
    {
        Id = id;
        ValidateUtc(createdAtUtc);
        Apply(details, isCreate: true);
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public string AssetTag { get; private set; } = string.Empty;

    public string AssetTagNormalized { get; private set; } = string.Empty;

    public string? DeviceName { get; private set; }

    public Guid CategoryId { get; private set; }

    public string? Description { get; private set; }

    public string? Manufacturer { get; private set; }

    public string? Model { get; private set; }

    public string? SerialNumber { get; private set; }

    public string? SerialNumberNormalized { get; private set; }

    // Import evidence only. This value is not a verified unique identifier.
    public string? ReportedSerialNumber { get; private set; }

    public string? LanMacAddress { get; private set; }

    public Guid? BiosGuid { get; private set; }

    public string? Ownership { get; private set; }

    public AssetStatus Status { get; private set; }

    public AssetCondition Condition { get; private set; }

    public Guid? LocationId { get; private set; }

    public DateOnly? PurchaseDate { get; private set; }

    public decimal? PurchaseCost { get; private set; }

    public string? Currency { get; private set; }

    public string? Supplier { get; private set; }

    public string? OperatingSystem { get; private set; }

    public string? OperatingSystemVersion { get; private set; }

    public bool? Windows11Compliant { get; private set; }

    public string? Notes { get; private set; }

    public string? LegacyId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? RetiredAtUtc { get; private set; }

    public string? RetirementReason { get; private set; }

    public DateTimeOffset? DisposedAtUtc { get; private set; }

    public string? DisposalReason { get; private set; }

    public Guid Version { get; private set; }

    public static Asset Create(AssetDetails details, DateTimeOffset createdAtUtc) =>
        new(Guid.NewGuid(), details, createdAtUtc);

    public bool SetReportedSerialNumber(
        string? reportedSerialNumber,
        Guid expectedVersion,
        DateTimeOffset updatedAtUtc)
    {
        EnsureVersion(expectedVersion);
        EnsureMutable();
        ValidateUtc(updatedAtUtc);
        var normalized = TextRules.Optional(
            reportedSerialNumber, nameof(ReportedSerialNumber), 200);
        if (ReportedSerialNumber == normalized)
        {
            return false;
        }

        ReportedSerialNumber = normalized;
        UpdatedAtUtc = updatedAtUtc;
        Version = Guid.NewGuid();
        return true;
    }

    public IReadOnlyDictionary<string, FieldChange> Update(
        AssetDetails details,
        Guid expectedVersion,
        DateTimeOffset updatedAtUtc)
    {
        EnsureVersion(expectedVersion);
        EnsureMutable();
        ValidateUtc(updatedAtUtc);

        var before = Snapshot();
        Apply(details, isCreate: false);
        var changes = BuildChanges(before, Snapshot());
        if (changes.Count == 0)
        {
            return changes;
        }

        UpdatedAtUtc = updatedAtUtc;
        Version = Guid.NewGuid();

        return changes;
    }

    public void Assign(Guid expectedVersion, DateTimeOffset assignedAtUtc)
    {
        EnsureVersion(expectedVersion);
        ValidateUtc(assignedAtUtc);

        if (Status is not (AssetStatus.InStock or AssetStatus.Reserved))
        {
            throw new DomainRuleException(
                "assignment_not_allowed",
                $"An asset with status {Status} cannot be assigned.");
        }

        Status = AssetStatus.Assigned;
        UpdatedAtUtc = assignedAtUtc;
        Version = Guid.NewGuid();
    }

    public void Transfer(Guid expectedVersion, DateTimeOffset transferredAtUtc)
    {
        EnsureVersion(expectedVersion);
        ValidateUtc(transferredAtUtc);

        if (Status != AssetStatus.Assigned)
        {
            throw new DomainRuleException(
                "assignment_not_active",
                "Only an assigned asset can be transferred.");
        }

        UpdatedAtUtc = transferredAtUtc;
        Version = Guid.NewGuid();
    }

    public void Return(
        Guid expectedVersion,
        DateTimeOffset returnedAtUtc,
        AssetCondition? condition,
        Guid? locationId)
    {
        EnsureVersion(expectedVersion);
        ValidateUtc(returnedAtUtc);

        if (Status != AssetStatus.Assigned)
        {
            throw new DomainRuleException(
                "assignment_not_active",
                "Only an assigned asset can be returned.");
        }

        Status = AssetStatus.InStock;
        if (condition.HasValue)
        {
            Condition = condition.Value;
        }

        if (locationId.HasValue)
        {
            LocationId = locationId.Value;
        }

        UpdatedAtUtc = returnedAtUtc;
        Version = Guid.NewGuid();
    }

    public void Retire(string reason, Guid expectedVersion, DateTimeOffset retiredAtUtc)
    {
        EnsureVersion(expectedVersion);
        ValidateUtc(retiredAtUtc);

        if (Status == AssetStatus.Disposed)
        {
            throw new DomainRuleException(
                "invalid_status_transition",
                "A disposed asset cannot be retired.");
        }

        if (Status == AssetStatus.Retired)
        {
            throw new DomainRuleException("already_retired", "The asset is already retired.");
        }

        RetirementReason = TextRules.Required(reason, nameof(RetirementReason), 1000);
        RetiredAtUtc = retiredAtUtc;
        Status = AssetStatus.Retired;
        UpdatedAtUtc = retiredAtUtc;
        Version = Guid.NewGuid();
    }

    public void Restore(Guid expectedVersion, DateTimeOffset restoredAtUtc)
    {
        EnsureVersion(expectedVersion);
        ValidateUtc(restoredAtUtc);

        if (Status != AssetStatus.Retired)
        {
            throw new DomainRuleException(
                "not_retired",
                "Only a retired asset can be restored.");
        }

        Status = AssetStatus.InStock;
        RetiredAtUtc = null;
        RetirementReason = null;
        UpdatedAtUtc = restoredAtUtc;
        Version = Guid.NewGuid();
    }

    public void Dispose(string reason, Guid expectedVersion, DateTimeOffset disposedAtUtc)
    {
        EnsureVersion(expectedVersion);
        ValidateUtc(disposedAtUtc);

        if (Status == AssetStatus.Disposed)
        {
            throw new DomainRuleException(
                "already_disposed",
                "The asset is already disposed.");
        }

        DisposalReason = TextRules.Required(reason, nameof(DisposalReason), 1000);
        DisposedAtUtc = disposedAtUtc;
        Status = AssetStatus.Disposed;
        UpdatedAtUtc = disposedAtUtc;
        Version = Guid.NewGuid();
    }

    private void Apply(AssetDetails details, bool isCreate)
    {
        if (details.CategoryId == Guid.Empty)
        {
            throw new DomainRuleException("category_required", "CategoryId is required.");
        }

        if (details.PurchaseCost < 0)
        {
            throw new DomainRuleException(
                "invalid_purchase_cost",
                "Purchase cost cannot be negative.");
        }

        if (details.PurchaseCost.HasValue &&
            decimal.Round(details.PurchaseCost.Value, 2) != details.PurchaseCost.Value)
        {
            throw new DomainRuleException(
                "invalid_purchase_cost",
                "Purchase cost cannot have more than two decimal places.");
        }

        if (details.PurchaseCost > MaximumPurchaseCost)
        {
            throw new DomainRuleException(
                "invalid_purchase_cost",
                "Purchase cost is outside the supported range.");
        }

        if (details.PurchaseCost.HasValue && string.IsNullOrWhiteSpace(details.Currency))
        {
            throw new DomainRuleException(
                "currency_required",
                "Currency is required when a purchase cost is supplied.");
        }

        if (details.Status == AssetStatus.Assigned && (isCreate || Status != AssetStatus.Assigned))
        {
            throw new DomainRuleException(
                "assignment_required",
                "Assigned status is managed by the assignment workflow.");
        }

        if (isCreate && details.Status is AssetStatus.Retired or AssetStatus.Disposed)
        {
            throw new DomainRuleException(
                "lifecycle_action_required",
                "Create the asset in an active status, then use the dedicated lifecycle action.");
        }

        if (!isCreate && Status == AssetStatus.Assigned && details.Status != AssetStatus.Assigned)
        {
            throw new DomainRuleException(
                "assignment_return_required",
                "Use the assignment return workflow to change an assigned asset's status.");
        }

        if (!isCreate && details.Status is AssetStatus.Retired or AssetStatus.Disposed)
        {
            throw new DomainRuleException(
                "lifecycle_action_required",
                "Use the dedicated lifecycle action for retired or disposed assets.");
        }

        AssetTag = TextRules.Required(details.AssetTag, nameof(AssetTag), 100);
        AssetTagNormalized = TextRules.NormalizeKey(AssetTag);
        DeviceName = TextRules.Optional(details.DeviceName, nameof(DeviceName), 150);
        CategoryId = details.CategoryId;
        Description = TextRules.Optional(details.Description, nameof(Description), 2000);
        Manufacturer = TextRules.Optional(details.Manufacturer, nameof(Manufacturer), 150);
        Model = TextRules.Optional(details.Model, nameof(Model), 150);
        SerialNumber = TextRules.Optional(details.SerialNumber, nameof(SerialNumber), 200);
        SerialNumberNormalized = SerialNumber is null ? null : TextRules.NormalizeKey(SerialNumber);
        LanMacAddress = NormalizeMacAddress(details.LanMacAddress);
        BiosGuid = details.BiosGuid;
        Ownership = TextRules.Optional(details.Ownership, nameof(Ownership), 100);
        Status = details.Status;
        Condition = details.Condition;
        LocationId = details.LocationId;
        PurchaseDate = details.PurchaseDate;
        PurchaseCost = details.PurchaseCost;
        Currency = TextRules.Optional(details.Currency, nameof(Currency), 3)?.ToUpperInvariant();
        Supplier = TextRules.Optional(details.Supplier, nameof(Supplier), 200);
        OperatingSystem = TextRules.Optional(
            details.OperatingSystem,
            nameof(OperatingSystem),
            100);
        OperatingSystemVersion = TextRules.Optional(
            details.OperatingSystemVersion,
            nameof(OperatingSystemVersion),
            100);
        Windows11Compliant = details.Windows11Compliant;
        Notes = TextRules.Optional(details.Notes, nameof(Notes), 4000);
        LegacyId = TextRules.Optional(details.LegacyId, nameof(LegacyId), 200);

        if (Currency is not null && Currency.Length != 3)
        {
            throw new DomainRuleException(
                "invalid_currency",
                "Currency must be a three-letter ISO code.");
        }
    }

    private Dictionary<string, object?> Snapshot() =>
        new(StringComparer.Ordinal)
        {
            [nameof(AssetTag)] = AssetTag,
            [nameof(DeviceName)] = DeviceName,
            [nameof(CategoryId)] = CategoryId,
            [nameof(Description)] = Description,
            [nameof(Manufacturer)] = Manufacturer,
            [nameof(Model)] = Model,
            [nameof(SerialNumber)] = SerialNumber,
            [nameof(LanMacAddress)] = LanMacAddress,
            [nameof(BiosGuid)] = BiosGuid,
            [nameof(Ownership)] = Ownership,
            [nameof(Status)] = Status,
            [nameof(Condition)] = Condition,
            [nameof(LocationId)] = LocationId,
            [nameof(PurchaseDate)] = PurchaseDate,
            [nameof(PurchaseCost)] = PurchaseCost,
            [nameof(Currency)] = Currency,
            [nameof(Supplier)] = Supplier,
            [nameof(OperatingSystem)] = OperatingSystem,
            [nameof(OperatingSystemVersion)] = OperatingSystemVersion,
            [nameof(Windows11Compliant)] = Windows11Compliant,
            [nameof(Notes)] = Notes,
            [nameof(LegacyId)] = LegacyId,
        };

    private static string? NormalizeMacAddress(string? value)
    {
        var trimmed = TextRules.Optional(value, nameof(LanMacAddress), 17);
        if (trimmed is null)
        {
            return null;
        }

        var compact = trimmed
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (compact.Length != 12 || compact.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new DomainRuleException(
                "invalid_lan_mac_address",
                "LAN MAC address must contain six hexadecimal octets.");
        }

        return string.Join(
            ":",
            Enumerable.Range(0, 6)
                .Select(index => compact.Substring(index * 2, 2).ToUpperInvariant()));
    }

    private static Dictionary<string, FieldChange> BuildChanges(
        Dictionary<string, object?> before,
        Dictionary<string, object?> after)
    {
        var changes = new Dictionary<string, FieldChange>(StringComparer.Ordinal);
        foreach (var field in before.Keys)
        {
            if (!Equals(before[field], after[field]))
            {
                changes[field] = new FieldChange(before[field], after[field]);
            }
        }

        return changes;
    }

    private void EnsureMutable()
    {
        if (Status is AssetStatus.Retired or AssetStatus.Disposed)
        {
            throw new DomainRuleException(
                "asset_not_mutable",
                "Retired or disposed assets cannot be edited through the normal update operation.");
        }
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The asset was changed by another user.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException("utc_required", "Asset timestamps must be UTC.");
        }
    }
}
