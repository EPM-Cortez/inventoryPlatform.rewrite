using System.Net.Mail;
using Inventory.Domain.Common;

namespace Inventory.Domain.People;

public enum PersonType
{
    Staff,
    Pupil,
    Contractor,
    Other,
}

public sealed class Person
{
    private Person()
    {
    }

    private Person(
        Guid id,
        string? externalId,
        string displayName,
        PersonType type,
        string? email)
    {
        Id = id;
        SetValues(externalId, displayName, type, email);
        IsActive = true;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public string? ExternalId { get; private set; }

    public string? ExternalIdNormalized { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public PersonType Type { get; private set; }

    public string? Email { get; private set; }

    public bool IsActive { get; private set; }

    public Guid Version { get; private set; }

    public static Person Create(
        string? externalId,
        string displayName,
        PersonType type,
        string? email = null) =>
        new(Guid.NewGuid(), externalId, displayName, type, email);

    public void Update(
        string? externalId,
        string displayName,
        PersonType type,
        string? email,
        bool isActive,
        Guid expectedVersion)
    {
        EnsureVersion(expectedVersion);
        SetValues(externalId, displayName, type, email);
        IsActive = isActive;
        Version = Guid.NewGuid();
    }

    private void SetValues(
        string? externalId,
        string displayName,
        PersonType type,
        string? email)
    {
        ExternalId = TextRules.Optional(externalId, nameof(ExternalId), 100);
        ExternalIdNormalized = ExternalId is null ? null : TextRules.NormalizeKey(ExternalId);
        DisplayName = TextRules.Required(displayName, nameof(DisplayName), 200);
        if (!Enum.IsDefined(type))
        {
            throw new DomainRuleException(
                "invalid_person_type",
                "Person type is not supported.");
        }

        Type = type;
        Email = TextRules.Optional(email, nameof(Email), 320);

        if (Email is not null && !MailAddress.TryCreate(Email, out _))
        {
            throw new DomainRuleException(
                "invalid_email",
                "Email must be a valid email address.");
        }
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The person was changed by another user.");
        }
    }
}
