using Inventory.Domain.Common;

namespace Inventory.Domain.Qr;

public sealed class QrIdentifier
{
    private QrIdentifier()
    {
    }

    private QrIdentifier(
        Guid id,
        Guid assetId,
        string tokenHash,
        string protectedToken,
        int tokenVersion,
        DateTimeOffset issuedAtUtc,
        Guid issuedByUserId,
        string issuedByDisplayName)
    {
        if (assetId == Guid.Empty || issuedByUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "invalid_qr_identifier",
                "QR asset and issuing user IDs are required.");
        }

        if (tokenVersion < 1)
        {
            throw new DomainRuleException(
                "invalid_qr_version",
                "QR token version must be positive.");
        }

        Id = id;
        AssetId = assetId;
        TokenHash = TextRules.Required(tokenHash, nameof(TokenHash), 64);
        ProtectedToken = TextRules.Required(protectedToken, nameof(ProtectedToken), 2048);
        TokenVersion = tokenVersion;
        IssuedAtUtc = ValidateUtc(issuedAtUtc);
        IssuedByUserId = issuedByUserId;
        IssuedByDisplayName = TextRules.Required(
            issuedByDisplayName,
            nameof(IssuedByDisplayName),
            200);
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid AssetId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public string ProtectedToken { get; private set; } = string.Empty;

    public int TokenVersion { get; private set; }

    public DateTimeOffset IssuedAtUtc { get; private set; }

    public Guid IssuedByUserId { get; private set; }

    public string IssuedByDisplayName { get; private set; } = string.Empty;

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    public string? RevokedByDisplayName { get; private set; }

    public string? RevocationReason { get; private set; }

    public Guid Version { get; private set; }

    public bool IsActive => RevokedAtUtc is null;

    public static QrIdentifier Issue(
        Guid assetId,
        string tokenHash,
        string protectedToken,
        int tokenVersion,
        DateTimeOffset issuedAtUtc,
        Guid issuedByUserId,
        string issuedByDisplayName) =>
        new(
            Guid.NewGuid(),
            assetId,
            tokenHash,
            protectedToken,
            tokenVersion,
            issuedAtUtc,
            issuedByUserId,
            issuedByDisplayName);

    public void Revoke(
        Guid expectedVersion,
        DateTimeOffset revokedAtUtc,
        Guid revokedByUserId,
        string revokedByDisplayName,
        string reason)
    {
        if (expectedVersion == Guid.Empty || expectedVersion != Version)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The QR identifier was changed by another user.");
        }

        if (!IsActive)
        {
            throw new DomainRuleException(
                "qr_already_revoked",
                "The QR identifier is already revoked.");
        }

        if (revokedByUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "invalid_qr_actor",
                "The revoking user ID is required.");
        }

        RevokedAtUtc = ValidateUtc(revokedAtUtc);
        RevokedByUserId = revokedByUserId;
        RevokedByDisplayName = TextRules.Required(
            revokedByDisplayName,
            nameof(RevokedByDisplayName),
            200);
        RevocationReason = TextRules.Required(reason, nameof(RevocationReason), 500);
        Version = Guid.NewGuid();
    }

    private static DateTimeOffset ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                "QR timestamps must be UTC.");
        }

        return value;
    }
}
