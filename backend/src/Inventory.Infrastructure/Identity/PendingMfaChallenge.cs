namespace Inventory.Infrastructure.Identity;

public sealed class PendingMfaChallenge
{
    private PendingMfaChallenge()
    {
    }

    public PendingMfaChallenge(
        string tokenHash,
        Guid userId,
        string securityStamp,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        TokenHash = tokenHash;
        UserId = userId;
        SecurityStamp = securityStamp;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string TokenHash { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }

    public string SecurityStamp { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }
}
