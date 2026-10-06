using Inventory.Domain.Common;

namespace Inventory.Domain.People;

public sealed class Department
{
    private Department()
    {
    }

    private Department(Guid id, string code, string name)
    {
        Id = id;
        SetValues(code, name);
        IsActive = true;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string CodeNormalized { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public Guid Version { get; private set; }

    public static Department Create(string code, string name) =>
        new(Guid.NewGuid(), code, name);

    public void Update(
        string code,
        string name,
        bool isActive,
        Guid expectedVersion)
    {
        EnsureVersion(expectedVersion);
        SetValues(code, name);
        IsActive = isActive;
        Version = Guid.NewGuid();
    }

    private void SetValues(string code, string name)
    {
        Code = TextRules.Required(code, nameof(Code), 50);
        CodeNormalized = TextRules.NormalizeKey(Code);
        Name = TextRules.Required(name, nameof(Name), 150);
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The department was changed by another user.");
        }
    }
}
