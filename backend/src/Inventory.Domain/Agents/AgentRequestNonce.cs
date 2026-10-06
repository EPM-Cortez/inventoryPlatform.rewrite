using Inventory.Domain.Common;

namespace Inventory.Domain.Agents;

public sealed class AgentRequestNonce
{
    private AgentRequestNonce()
    {
    }

    private AgentRequestNonce(
        AgentRequestNonceScope scope,
        Guid contextId,
        string nonceHash,
        DateTimeOffset receivedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        ValidateUtc(receivedAtUtc, nameof(receivedAtUtc));
        ValidateUtc(expiresAtUtc, nameof(expiresAtUtc));
        if (contextId == Guid.Empty)
        {
            throw new DomainRuleException(
                "agent_nonce_context_required",
                "An endpoint nonce context is required.");
        }

        if (expiresAtUtc <= receivedAtUtc)
        {
            throw new DomainRuleException(
                "agent_nonce_expiry_invalid",
                "The endpoint nonce expiry must follow receipt.");
        }

        var normalizedHash = TextRules.Required(
            nonceHash,
            nameof(NonceHash),
            44);
        if (normalizedHash.Length != 44)
        {
            throw new DomainRuleException(
                "agent_nonce_hash_invalid",
                "The endpoint nonce hash is invalid.");
        }

        Id = Guid.NewGuid();
        Scope = scope;
        ContextId = contextId;
        NonceHash = normalizedHash;
        ReceivedAtUtc = receivedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public AgentRequestNonceScope Scope { get; private set; }

    public Guid ContextId { get; private set; }

    public string NonceHash { get; private set; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public static AgentRequestNonce Create(
        AgentRequestNonceScope scope,
        Guid contextId,
        string nonceHash,
        DateTimeOffset receivedAtUtc,
        DateTimeOffset expiresAtUtc) =>
        new(
            scope,
            contextId,
            nonceHash,
            receivedAtUtc,
            expiresAtUtc);

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
