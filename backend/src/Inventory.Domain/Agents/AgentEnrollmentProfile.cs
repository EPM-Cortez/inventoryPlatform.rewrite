using Inventory.Domain.Common;

namespace Inventory.Domain.Agents;

public sealed class AgentEnrollmentProfile
{
    private AgentEnrollmentProfile()
    {
    }

    private AgentEnrollmentProfile(
        Guid id,
        string name,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        int maxEnrollments,
        bool requireTpm,
        string? allowedHostnameSuffix,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        ValidateUtc(createdAtUtc, nameof(createdAtUtc));
        ValidateUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (id == Guid.Empty || createdByUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "agent_enrollment_profile_identity_required",
                "Enrollment profile and creator identifiers are required.");
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new DomainRuleException(
                "agent_enrollment_profile_expiry_invalid",
                "The enrollment profile must expire in the future.");
        }

        if (maxEnrollments is < 1 or > 5000)
        {
            throw new DomainRuleException(
                "agent_enrollment_profile_limit_invalid",
                "The enrollment limit must be between 1 and 5000.");
        }

        Id = id;
        Name = TextRules.Required(name, nameof(Name), 150);
        TokenHash = TextRules.Required(tokenHash, nameof(TokenHash), 44);
        ExpiresAtUtc = expiresAtUtc;
        MaxEnrollments = maxEnrollments;
        RequireTpm = requireTpm;
        AllowedHostnameSuffix = NormalizeHostnameSuffix(allowedHostnameSuffix);
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public int MaxEnrollments { get; private set; }

    public int EnrolledCount { get; private set; }

    public bool RequireTpm { get; private set; }

    public string? AllowedHostnameSuffix { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid Version { get; private set; }

    public static AgentEnrollmentProfile Create(
        Guid id,
        string name,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        int maxEnrollments,
        bool requireTpm,
        string? allowedHostnameSuffix,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc) =>
        new(
            id,
            name,
            tokenHash,
            expiresAtUtc,
            maxEnrollments,
            requireTpm,
            allowedHostnameSuffix,
            createdByUserId,
            createdAtUtc);

    public void Consume(
        string hostname,
        AgentKeyProtection keyProtection,
        DateTimeOffset consumedAtUtc)
    {
        ValidateUtc(consumedAtUtc, nameof(consumedAtUtc));
        if (RevokedAtUtc.HasValue)
        {
            throw new DomainRuleException(
                "agent_enrollment_profile_revoked",
                "The enrollment profile has been revoked.");
        }

        if (consumedAtUtc >= ExpiresAtUtc)
        {
            throw new DomainRuleException(
                "agent_enrollment_profile_expired",
                "The enrollment profile has expired.");
        }

        if (EnrolledCount >= MaxEnrollments)
        {
            throw new DomainRuleException(
                "agent_enrollment_profile_exhausted",
                "The enrollment profile has reached its device limit.");
        }

        if (RequireTpm && keyProtection != AgentKeyProtection.Tpm)
        {
            throw new DomainRuleException(
                "agent_tpm_required",
                "This enrollment profile requires a TPM-backed device key.");
        }

        if (AllowedHostnameSuffix is not null &&
            !hostname.Trim().EndsWith(
                AllowedHostnameSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleException(
                "agent_hostname_not_allowed",
                "The device hostname is outside this enrollment profile.");
        }

        EnrolledCount += 1;
        Version = Guid.NewGuid();
    }

    public void Revoke(Guid expectedVersion, DateTimeOffset revokedAtUtc)
    {
        EnsureVersion(expectedVersion);
        ValidateUtc(revokedAtUtc, nameof(revokedAtUtc));
        if (RevokedAtUtc.HasValue)
        {
            throw new DomainRuleException(
                "agent_enrollment_profile_already_revoked",
                "The enrollment profile is already revoked.");
        }

        RevokedAtUtc = revokedAtUtc;
        Version = Guid.NewGuid();
    }

    private static string? NormalizeHostnameSuffix(string? value)
    {
        var suffix = TextRules.Optional(
            value,
            nameof(AllowedHostnameSuffix),
            150);
        if (suffix is null)
        {
            return null;
        }

        suffix = suffix.Trim().TrimStart('.');
        if (suffix.Length == 0 ||
            suffix.Any(
                character =>
                    !(char.IsAsciiLetterOrDigit(character) ||
                      character is '-' or '.')))
        {
            throw new DomainRuleException(
                "agent_hostname_suffix_invalid",
                "The allowed hostname suffix is invalid.");
        }

        return "." + suffix.ToLowerInvariant();
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The enrollment profile was changed by another request.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                $"{name} must be UTC.");
        }
    }
}
