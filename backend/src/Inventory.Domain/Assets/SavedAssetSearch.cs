using Inventory.Domain.Common;

namespace Inventory.Domain.Assets;

public sealed class SavedAssetSearch
{
    public const int CurrentSchemaVersion = 1;

    private SavedAssetSearch()
    {
    }

    private SavedAssetSearch(
        Guid id,
        Guid userId,
        string name,
        int schemaVersion,
        string filterJson,
        DateTimeOffset createdAtUtc)
    {
        ValidateUser(userId);
        ValidateSchemaVersion(schemaVersion);
        ValidateUtc(createdAtUtc);
        Id = id;
        UserId = userId;
        SetValues(name, filterJson);
        SchemaVersion = schemaVersion;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string NameNormalized { get; private set; } = string.Empty;

    public int SchemaVersion { get; private set; }

    public string FilterJson { get; private set; } = string.Empty;

    public bool IsDefault { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public Guid Version { get; private set; }

    public static SavedAssetSearch Create(
        Guid userId,
        string name,
        int schemaVersion,
        string filterJson,
        DateTimeOffset createdAtUtc) =>
        new(
            Guid.NewGuid(),
            userId,
            name,
            schemaVersion,
            filterJson,
            createdAtUtc);

    public void Update(
        string name,
        int schemaVersion,
        string filterJson,
        Guid expectedVersion,
        DateTimeOffset updatedAtUtc)
    {
        EnsureActive();
        EnsureVersion(expectedVersion);
        ValidateSchemaVersion(schemaVersion);
        ValidateUtc(updatedAtUtc);
        SetValues(name, filterJson);
        SchemaVersion = schemaVersion;
        UpdatedAtUtc = updatedAtUtc;
        Version = Guid.NewGuid();
    }

    public void SetDefault(
        bool isDefault,
        Guid expectedVersion,
        DateTimeOffset updatedAtUtc)
    {
        EnsureActive();
        EnsureVersion(expectedVersion);
        ValidateUtc(updatedAtUtc);
        IsDefault = isDefault;
        UpdatedAtUtc = updatedAtUtc;
        Version = Guid.NewGuid();
    }

    public void Archive(
        Guid expectedVersion,
        DateTimeOffset archivedAtUtc)
    {
        EnsureActive();
        EnsureVersion(expectedVersion);
        ValidateUtc(archivedAtUtc);
        IsDefault = false;
        ArchivedAtUtc = archivedAtUtc;
        UpdatedAtUtc = archivedAtUtc;
        Version = Guid.NewGuid();
    }

    private void SetValues(string name, string filterJson)
    {
        Name = TextRules.Required(name, nameof(Name), 100);
        NameNormalized = TextRules.NormalizeKey(Name);
        FilterJson = TextRules.Required(filterJson, nameof(FilterJson), 16000);
    }

    private void EnsureActive()
    {
        if (ArchivedAtUtc.HasValue)
        {
            throw new DomainRuleException(
                "saved_search_archived",
                "An archived saved search cannot be changed.");
        }
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The saved search was changed by another request.");
        }
    }

    private static void ValidateUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainRuleException(
                "user_required",
                "A saved search must identify its owner.");
        }
    }

    private static void ValidateSchemaVersion(int schemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new DomainRuleException(
                "unsupported_saved_search_schema",
                $"Saved search schema version must be {CurrentSchemaVersion}.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                "Saved search timestamps must be UTC.");
        }
    }
}
