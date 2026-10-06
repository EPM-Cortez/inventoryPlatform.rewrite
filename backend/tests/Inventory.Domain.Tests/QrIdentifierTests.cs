using Inventory.Domain.Common;
using Inventory.Domain.Qr;

namespace Inventory.Domain.Tests;

public sealed class QrIdentifierTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ActorId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void RotationMetadataRevokesWithoutChangingTheIssuedIdentity()
    {
        var identifier = QrIdentifier.Issue(
            Guid.NewGuid(),
            "hashed-token",
            "protected-token",
            1,
            Now,
            ActorId,
            "Test Administrator");
        var issuedVersion = identifier.Version;

        identifier.Revoke(
            issuedVersion,
            Now.AddMinutes(1),
            ActorId,
            "Test Administrator",
            "Damaged label");

        Assert.False(identifier.IsActive);
        Assert.Equal(Now.AddMinutes(1), identifier.RevokedAtUtc);
        Assert.Equal("Damaged label", identifier.RevocationReason);
        Assert.NotEqual(issuedVersion, identifier.Version);
    }

    [Fact]
    public void RejectsAStaleOrRepeatedRevocation()
    {
        var identifier = QrIdentifier.Issue(
            Guid.NewGuid(),
            "hashed-token",
            "protected-token",
            1,
            Now,
            ActorId,
            "Test Administrator");

        var stale = Assert.Throws<DomainRuleException>(
            () => identifier.Revoke(
                Guid.NewGuid(),
                Now.AddMinutes(1),
                ActorId,
                "Test Administrator",
                "Stale"));
        Assert.Equal("concurrency_conflict", stale.Code);

        identifier.Revoke(
            identifier.Version,
            Now.AddMinutes(1),
            ActorId,
            "Test Administrator",
            "Rotated");
        var repeated = Assert.Throws<DomainRuleException>(
            () => identifier.Revoke(
                identifier.Version,
                Now.AddMinutes(2),
                ActorId,
                "Test Administrator",
                "Repeated"));
        Assert.Equal("qr_already_revoked", repeated.Code);
    }
}
