using Inventory.Domain.Agents;
using Inventory.Domain.Common;

namespace Inventory.Domain.Tests;

public sealed class AgentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EnrollmentProfileEnforcesTpmSuffixAndUsageCap()
    {
        var profile = CreateProfile(
            maxEnrollments: 1,
            requireTpm: true,
            hostnameSuffix: "school.example");

        var keyException = Assert.Throws<DomainRuleException>(
            () => profile.Consume(
                "pc01.school.example",
                AgentKeyProtection.Software,
                Now.AddMinutes(1)));
        Assert.Equal("agent_tpm_required", keyException.Code);

        var suffixException = Assert.Throws<DomainRuleException>(
            () => profile.Consume(
                "pc01.other.example",
                AgentKeyProtection.Tpm,
                Now.AddMinutes(1)));
        Assert.Equal("agent_hostname_not_allowed", suffixException.Code);

        profile.Consume(
            "pc01.school.example",
            AgentKeyProtection.Tpm,
            Now.AddMinutes(1));
        Assert.Equal(1, profile.EnrolledCount);

        var limitException = Assert.Throws<DomainRuleException>(
            () => profile.Consume(
                "pc02.school.example",
                AgentKeyProtection.Tpm,
                Now.AddMinutes(2)));
        Assert.Equal(
            "agent_enrollment_profile_exhausted",
            limitException.Code);
    }

    [Fact]
    public void EnrollmentProfileRejectsUseAtExpiryAndAfterRevocation()
    {
        var expired = CreateProfile(expiresAtUtc: Now.AddMinutes(5));
        var expiryException = Assert.Throws<DomainRuleException>(
            () => expired.Consume(
                "pc01",
                AgentKeyProtection.Tpm,
                Now.AddMinutes(5)));
        Assert.Equal(
            "agent_enrollment_profile_expired",
            expiryException.Code);

        var revoked = CreateProfile();
        revoked.Revoke(revoked.Version, Now.AddMinutes(1));
        var revokedException = Assert.Throws<DomainRuleException>(
            () => revoked.Consume(
                "pc01",
                AgentKeyProtection.Tpm,
                Now.AddMinutes(2)));
        Assert.Equal(
            "agent_enrollment_profile_revoked",
            revokedException.Code);
    }

    [Fact]
    public void ManagedAgentRequiresIncreasingSequenceAndRejectsAfterRevocation()
    {
        var agent = CreateAgent();
        agent.RecordCheckIn(
            1,
            Guid.NewGuid(),
            Now.AddMinutes(1),
            Now.AddMinutes(2),
            "1.0.0",
            "pc01.school.example",
            "serial-1",
            Guid.NewGuid().ToString(),
            "Maker",
            "Model",
            "192.0.2.10");
        Assert.Equal(1, agent.LastSequence);

        var replayException = Assert.Throws<DomainRuleException>(
            () => agent.RecordCheckIn(
                1,
                Guid.NewGuid(),
                Now.AddMinutes(2),
                Now.AddMinutes(3),
                "1.0.0",
                "pc01.school.example",
                null,
                null,
                null,
                null,
                null));
        Assert.Equal("agent_sequence_replayed", replayException.Code);

        agent.Revoke(
            "Device retired",
            agent.Version,
            Now.AddMinutes(4));
        var revokedException = Assert.Throws<DomainRuleException>(
            () => agent.RecordCheckIn(
                2,
                Guid.NewGuid(),
                Now.AddMinutes(4),
                Now.AddMinutes(5),
                "1.0.0",
                "pc01.school.example",
                null,
                null,
                null,
                null,
                null));
        Assert.Equal("agent_revoked", revokedException.Code);
    }

    [Fact]
    public void DismissedAgentSkipsAutomaticMatchingAndCanReturnToReview()
    {
        var agent = CreateAgent();
        agent.MarkMatchConflict("Multiple possible assets");
        Assert.Equal(ManagedAgentMatchState.Conflict, agent.MatchState);

        agent.DismissMatch("Temporary lab image", agent.Version);
        var dismissedVersion = agent.Version;
        Assert.Equal(ManagedAgentMatchState.Dismissed, agent.MatchState);

        agent.AutoLinkAsset(Guid.NewGuid(), "Automatic serial match");
        Assert.Null(agent.LinkedAssetId);
        Assert.Equal(dismissedVersion, agent.Version);

        agent.RestoreMatchReview(
            "Machine has now been identified",
            agent.Version);
        Assert.Equal(ManagedAgentMatchState.Unmatched, agent.MatchState);

        var assetId = Guid.NewGuid();
        agent.AutoLinkAsset(assetId, "Automatic serial match");
        Assert.Equal(assetId, agent.LinkedAssetId);
        Assert.Equal(ManagedAgentMatchState.Matched, agent.MatchState);
    }

    [Fact]
    public void LinkedAgentCanBeUnlinkedWithOptimisticConcurrency()
    {
        var agent = CreateAgent();
        var assetId = Guid.NewGuid();
        agent.LinkAsset(
            assetId,
            "Administrator confirmed the device",
            agent.Version);

        var staleVersion = agent.Version;
        agent.UnlinkAsset(
            "Asset record was selected in error",
            agent.Version);
        Assert.Null(agent.LinkedAssetId);
        Assert.Equal(ManagedAgentMatchState.Conflict, agent.MatchState);
        Assert.Contains(
            "selected in error",
            agent.MatchReason,
            StringComparison.Ordinal);

        agent.AutoLinkAsset(assetId, "Automatic serial match");
        Assert.Null(agent.LinkedAssetId);
        Assert.Equal(ManagedAgentMatchState.Conflict, agent.MatchState);

        var conflict = Assert.Throws<DomainRuleException>(
            () => agent.DismissMatch("Hide row", staleVersion));
        Assert.Equal("concurrency_conflict", conflict.Code);
    }

    [Fact]
    public void RevocationAtomicallyReleasesLinkedAsset()
    {
        var agent = CreateAgent();
        var assetId = Guid.NewGuid();
        agent.LinkAsset(
            assetId,
            "Administrator confirmed the device",
            agent.Version);

        agent.Revoke(
            "Device trust was withdrawn",
            agent.Version,
            Now.AddMinutes(1));

        Assert.True(agent.IsRevoked);
        Assert.Null(agent.LinkedAssetId);
        Assert.Equal(ManagedAgentMatchState.Conflict, agent.MatchState);
        Assert.Contains(
            "trust was revoked",
            agent.MatchReason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApprovedKeyRecoveryReplacesRevokedKeyAndPreservesSequence()
    {
        var agent = CreateAgent();
        agent.RecordCheckIn(
            12,
            Guid.NewGuid(),
            Now.AddMinutes(1),
            Now.AddMinutes(2),
            "1.0.0",
            "pc01.school.example",
            "serial-1",
            Guid.NewGuid().ToString(),
            "Maker",
            "Model",
            "192.0.2.10");
        agent.ApproveKeyRecovery(
            "TPM was cleared during repair",
            agent.Version,
            Now.AddMinutes(3));
        var approvedVersion = agent.Version;

        Assert.True(agent.IsRevoked);
        agent.ReplaceKey(
            "replacement-public-key",
            new string('b', 64),
            AgentKeyProtection.Tpm,
            "pc01.school.example",
            "serial-1",
            Guid.NewGuid().ToString(),
            "Maker",
            "Model",
            approvedVersion,
            Now.AddMinutes(4));

        Assert.False(agent.IsRevoked);
        Assert.Equal(12, agent.LastSequence);
        Assert.Equal(new string('b', 64), agent.KeyThumbprintSha256);
        Assert.Equal(1, agent.KeyReplacementCount);
        Assert.Equal(Now.AddMinutes(4), agent.KeyReplacedAtUtc);
        Assert.Equal(ManagedAgentMatchState.Conflict, agent.MatchState);
    }

    [Fact]
    public void ManagedAgentEnforcesPersistedBurstAndQuotaWindows()
    {
        var agent = CreateAgent();
        for (var sequence = 1; sequence <= 2; sequence++)
        {
            agent.RecordCheckIn(
                sequence,
                Guid.NewGuid(),
                Now.AddMinutes(sequence),
                Now.AddMinutes(3),
                "1.0.0",
                "pc01.school.example",
                "serial-1",
                Guid.NewGuid().ToString(),
                "Maker",
                "Model",
                "192.0.2.10",
                TimeSpan.FromMinutes(15),
                burstLimit: 2,
                quotaWindow: TimeSpan.FromHours(24),
                quotaLimit: 3);
        }

        var burst = Assert.Throws<DomainRuleException>(
            () => agent.RecordCheckIn(
                3,
                Guid.NewGuid(),
                Now.AddMinutes(3),
                Now.AddMinutes(3),
                "1.0.0",
                "pc01.school.example",
                "serial-1",
                Guid.NewGuid().ToString(),
                "Maker",
                "Model",
                "192.0.2.10",
                TimeSpan.FromMinutes(15),
                burstLimit: 2,
                quotaWindow: TimeSpan.FromHours(24),
                quotaLimit: 3));
        Assert.Equal("agent_check_in_burst_exceeded", burst.Code);

        agent.RecordCheckIn(
            3,
            Guid.NewGuid(),
            Now.AddMinutes(20),
            Now.AddMinutes(20),
            "1.0.0",
            "pc01.school.example",
            "serial-1",
            Guid.NewGuid().ToString(),
            "Maker",
            "Model",
            "192.0.2.10",
            TimeSpan.FromMinutes(15),
            burstLimit: 2,
            quotaWindow: TimeSpan.FromHours(24),
            quotaLimit: 3);
        var quota = Assert.Throws<DomainRuleException>(
            () => agent.RecordCheckIn(
                4,
                Guid.NewGuid(),
                Now.AddMinutes(21),
                Now.AddMinutes(21),
                "1.0.0",
                "pc01.school.example",
                "serial-1",
                Guid.NewGuid().ToString(),
                "Maker",
                "Model",
                "192.0.2.10",
                TimeSpan.FromMinutes(15),
                burstLimit: 2,
                quotaWindow: TimeSpan.FromHours(24),
                quotaLimit: 3));
        Assert.Equal("agent_check_in_quota_exceeded", quota.Code);
    }

    [Fact]
    public void KeyRecoveryAuthorizationIsTpmBoundAndSingleUse()
    {
        var userId = Guid.NewGuid();
        var authorization = AgentKeyRecoveryAuthorization.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Convert.ToBase64String(new byte[32]),
            "TPM replacement",
            requireTpm: true,
            expectedAgentVersion: Guid.NewGuid(),
            createdByUserId: userId,
            createdByDisplayName: "Administrator",
            createdAtUtc: Now,
            expiresAtUtc: Now.AddMinutes(30));

        var software = Assert.Throws<DomainRuleException>(
            () => authorization.Consume(
                AgentKeyProtection.Software,
                new string('b', 64),
                Now.AddMinutes(1)));
        Assert.Equal("agent_tpm_required", software.Code);

        authorization.Consume(
            AgentKeyProtection.Tpm,
            new string('b', 64),
            Now.AddMinutes(1));
        var replay = Assert.Throws<DomainRuleException>(
            () => authorization.Consume(
                AgentKeyProtection.Tpm,
                new string('c', 64),
                Now.AddMinutes(2)));
        Assert.Equal(
            "agent_key_recovery_authorization_invalid",
            replay.Code);

        var expired = AgentKeyRecoveryAuthorization.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Convert.ToBase64String(new byte[32]),
            "Expired TPM replacement",
            requireTpm: true,
            expectedAgentVersion: Guid.NewGuid(),
            createdByUserId: userId,
            createdByDisplayName: "Administrator",
            createdAtUtc: Now,
            expiresAtUtc: Now.AddMinutes(5));
        var expiry = Assert.Throws<DomainRuleException>(
            () => expired.Consume(
                AgentKeyProtection.Tpm,
                new string('c', 64),
                Now.AddMinutes(5)));
        Assert.Equal(
            "agent_key_recovery_authorization_invalid",
            expiry.Code);
    }

    [Fact]
    public void RequestNonceRequiresBoundedLifetimeAndValidContext()
    {
        var nonce = AgentRequestNonce.Create(
            AgentRequestNonceScope.CheckIn,
            Guid.NewGuid(),
            new string('A', 44),
            Now,
            Now.AddMinutes(10));
        Assert.Equal(AgentRequestNonceScope.CheckIn, nonce.Scope);

        var contextException = Assert.Throws<DomainRuleException>(
            () => AgentRequestNonce.Create(
                AgentRequestNonceScope.Enrollment,
                Guid.Empty,
                new string('A', 44),
                Now,
                Now.AddMinutes(10)));
        Assert.Equal("agent_nonce_context_required", contextException.Code);

        var expiryException = Assert.Throws<DomainRuleException>(
            () => AgentRequestNonce.Create(
                AgentRequestNonceScope.CheckIn,
                Guid.NewGuid(),
                new string('A', 44),
                Now,
                Now));
        Assert.Equal("agent_nonce_expiry_invalid", expiryException.Code);
    }

    private static AgentEnrollmentProfile CreateProfile(
        int maxEnrollments = 5,
        bool requireTpm = true,
        string? hostnameSuffix = null,
        DateTimeOffset? expiresAtUtc = null) =>
        AgentEnrollmentProfile.Create(
            Guid.NewGuid(),
            "Deployment",
            Convert.ToBase64String(new byte[32]),
            expiresAtUtc ?? Now.AddHours(1),
            maxEnrollments,
            requireTpm,
            hostnameSuffix,
            Guid.NewGuid(),
            Now);

    private static ManagedAgent CreateAgent() =>
        ManagedAgent.Enroll(
            Guid.NewGuid(),
            "win-device",
            "public-key",
            new string('a', 64),
            AgentKeyProtection.Tpm,
            "pc01.school.example",
            "serial-1",
            Guid.NewGuid().ToString(),
            "Maker",
            "Model",
            Now);
}
