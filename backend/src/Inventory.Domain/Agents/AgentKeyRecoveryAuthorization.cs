using Inventory.Domain.Common;

namespace Inventory.Domain.Agents;

public sealed class AgentKeyRecoveryAuthorization
{
    private AgentKeyRecoveryAuthorization()
    {
    }

    private AgentKeyRecoveryAuthorization(
        Guid id,
        Guid agentId,
        string tokenHash,
        string reason,
        bool requireTpm,
        Guid expectedAgentVersion,
        Guid createdByUserId,
        string createdByDisplayName,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        ValidateUtc(createdAtUtc, nameof(createdAtUtc));
        ValidateUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (id == Guid.Empty ||
            agentId == Guid.Empty ||
            expectedAgentVersion == Guid.Empty ||
            createdByUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "agent_key_recovery_identity_required",
                "Agent, version and approving administrator are required.");
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new DomainRuleException(
                "agent_key_recovery_expiry_invalid",
                "The key-recovery authorization must expire after it is created.");
        }

        Id = id;
        AgentId = agentId;
        TokenHash = TextRules.Required(tokenHash, nameof(TokenHash), 44);
        Reason = TextRules.Required(reason, nameof(Reason), 500);
        RequireTpm = requireTpm;
        ExpectedAgentVersion = expectedAgentVersion;
        CreatedByUserId = createdByUserId;
        CreatedByDisplayName = TextRules.Required(
            createdByDisplayName,
            nameof(CreatedByDisplayName),
            200);
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid AgentId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public string Reason { get; private set; } = string.Empty;

    public bool RequireTpm { get; private set; }

    public Guid ExpectedAgentVersion { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public string CreatedByDisplayName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }

    public string? ConsumedKeyThumbprintSha256 { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    public string? RevokedReason { get; private set; }

    public Guid Version { get; private set; }

    public bool IsPending => !ConsumedAtUtc.HasValue && !RevokedAtUtc.HasValue;

    public static AgentKeyRecoveryAuthorization Create(
        Guid id,
        Guid agentId,
        string tokenHash,
        string reason,
        bool requireTpm,
        Guid expectedAgentVersion,
        Guid createdByUserId,
        string createdByDisplayName,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc) =>
        new(
            id,
            agentId,
            tokenHash,
            reason,
            requireTpm,
            expectedAgentVersion,
            createdByUserId,
            createdByDisplayName,
            createdAtUtc,
            expiresAtUtc);

    public void Consume(
        AgentKeyProtection keyProtection,
        string keyThumbprintSha256,
        DateTimeOffset consumedAtUtc)
    {
        ValidateUtc(consumedAtUtc, nameof(consumedAtUtc));
        if (!IsPending || consumedAtUtc >= ExpiresAtUtc)
        {
            throw new DomainRuleException(
                "agent_key_recovery_authorization_invalid",
                "The key-recovery authorization is not available.");
        }

        if (RequireTpm && keyProtection != AgentKeyProtection.Tpm)
        {
            throw new DomainRuleException(
                "agent_tpm_required",
                "This key-recovery authorization requires TPM-backed key protection.");
        }

        ConsumedKeyThumbprintSha256 = TextRules.Required(
                keyThumbprintSha256,
                nameof(ConsumedKeyThumbprintSha256),
                64)
            .ToLowerInvariant();
        ConsumedAtUtc = consumedAtUtc;
        Version = Guid.NewGuid();
    }

    public void Revoke(
        Guid expectedVersion,
        Guid revokedByUserId,
        string reason,
        DateTimeOffset revokedAtUtc)
    {
        EnsureVersion(expectedVersion);
        ValidateUtc(revokedAtUtc, nameof(revokedAtUtc));
        if (!IsPending)
        {
            throw new DomainRuleException(
                "agent_key_recovery_authorization_closed",
                "The key-recovery authorization is already closed.");
        }

        if (revokedByUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "agent_key_recovery_revoker_required",
                "The administrator revoking key recovery is required.");
        }

        RevokedAtUtc = revokedAtUtc;
        RevokedByUserId = revokedByUserId;
        RevokedReason = TextRules.Required(
            reason,
            nameof(RevokedReason),
            500);
        Version = Guid.NewGuid();
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || expectedVersion != Version)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The key-recovery authorization was changed by another request.");
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
