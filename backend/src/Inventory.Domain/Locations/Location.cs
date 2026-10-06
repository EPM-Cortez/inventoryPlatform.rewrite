using Inventory.Domain.Common;

namespace Inventory.Domain.Locations;

public sealed class Location
{
    private Location()
    {
    }

    private Location(
        Guid id,
        string code,
        string name,
        string? type,
        Guid? parentLocationId)
    {
        Id = id;
        SetValues(code, name, type, parentLocationId);
        IsActive = true;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string CodeNormalized { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Type { get; private set; }

    public Guid? ParentLocationId { get; private set; }

    public bool IsActive { get; private set; }

    public Guid Version { get; private set; }

    public static Location Create(
        string code,
        string name,
        string? type = null,
        Guid? parentLocationId = null) =>
        new(Guid.NewGuid(), code, name, type, parentLocationId);

    public void Update(
        string code,
        string name,
        string? type,
        Guid? parentLocationId,
        bool isActive,
        Guid expectedVersion)
    {
        EnsureVersion(expectedVersion);
        SetValues(code, name, type, parentLocationId);
        IsActive = isActive;
        Version = Guid.NewGuid();
    }

    private void SetValues(string code, string name, string? type, Guid? parentLocationId)
    {
        if (parentLocationId == Id && Id != Guid.Empty)
        {
            throw new DomainRuleException(
                "invalid_parent",
                "A location cannot be its own parent.");
        }

        Code = TextRules.Required(code, nameof(Code), 50);
        CodeNormalized = TextRules.NormalizeKey(Code);
        Name = TextRules.Required(name, nameof(Name), 150);
        Type = TextRules.Optional(type, nameof(Type), 80);
        ParentLocationId = parentLocationId;
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The location was changed by another user.");
        }
    }
}
