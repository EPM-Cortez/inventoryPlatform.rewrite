using Inventory.Domain.Common;

namespace Inventory.Domain.Assets;

public sealed class AssetCategory
{
    private AssetCategory()
    {
    }

    private AssetCategory(Guid id, string code, string name, string? description)
    {
        Id = id;
        SetValues(code, name, description);
        IsActive = true;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string CodeNormalized { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public Guid Version { get; private set; }

    public static AssetCategory Create(string code, string name, string? description = null) =>
        new(Guid.NewGuid(), code, name, description);

    public void Update(string code, string name, string? description, bool isActive, Guid expectedVersion)
    {
        EnsureVersion(expectedVersion);
        SetValues(code, name, description);
        IsActive = isActive;
        Version = Guid.NewGuid();
    }

    private void SetValues(string code, string name, string? description)
    {
        Code = TextRules.Required(code, nameof(Code), 50);
        CodeNormalized = TextRules.NormalizeKey(Code);
        Name = TextRules.Required(name, nameof(Name), 150);
        Description = TextRules.Optional(description, nameof(Description), 1000);
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The category was changed by another user.");
        }
    }
}
