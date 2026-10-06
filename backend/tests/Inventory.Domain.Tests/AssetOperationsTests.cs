using Inventory.Domain.AssetTags;
using Inventory.Domain.BulkChanges;
using Inventory.Domain.Bundles;
using Inventory.Domain.Common;

namespace Inventory.Domain.Tests;

public sealed class AssetOperationsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AssetTagPolicyFormatsAndVersionControlsSequence()
    {
        var policy = AssetTagPolicy.Create("SCH-", 5, 42);
        var originalVersion = policy.Version;

        Assert.Equal("SCH-00042", policy.PreviewNextTag());

        policy.Update("ICT-", 6, 100, originalVersion);
        Assert.Equal("ICT-000100", policy.PreviewNextTag());
        Assert.NotEqual(originalVersion, policy.Version);

        var stale = Assert.Throws<DomainRuleException>(
            () => policy.Update("STALE-", 6, 1, originalVersion));
        Assert.Equal("concurrency_conflict", stale.Code);
    }

    [Fact]
    public void AssetTagPolicyRejectsSequenceOutsideNumericWidth()
    {
        var exception = Assert.Throws<DomainRuleException>(
            () => AssetTagPolicy.Create("SCH-", 2, 100));

        Assert.Equal("asset_tag_sequence_exhausted", exception.Code);
    }

    [Fact]
    public void BulkChangePreviewRequiresExactFingerprintVersionAndExpiry()
    {
        var job = CreateBulkJob();
        var stale = Assert.Throws<DomainRuleException>(
            () => job.Commit(Guid.NewGuid(), "fingerprint", Now.AddMinutes(1)));
        Assert.Equal("concurrency_conflict", stale.Code);

        var fingerprint = Assert.Throws<DomainRuleException>(
            () => job.Commit(job.Version, "changed", Now.AddMinutes(1)));
        Assert.Equal("bulk_preview_fingerprint_mismatch", fingerprint.Code);

        var expired = Assert.Throws<DomainRuleException>(
            () => job.Commit(job.Version, "fingerprint", Now.AddMinutes(30)));
        Assert.Equal("bulk_preview_expired", expired.Code);
        Assert.Equal(AssetBulkChangeJobStatus.Previewed, job.Status);
    }

    [Fact]
    public void BulkChangePreviewCanCommitOnlyOnce()
    {
        var job = CreateBulkJob();

        job.Commit(job.Version, "fingerprint", Now.AddMinutes(1));

        Assert.Equal(AssetBulkChangeJobStatus.Committed, job.Status);
        Assert.Equal(Now.AddMinutes(1), job.CommittedAtUtc);
        var replay = Assert.Throws<DomainRuleException>(
            () => job.Commit(job.Version, "fingerprint", Now.AddMinutes(2)));
        Assert.Equal("bulk_preview_already_committed", replay.Code);
    }

    [Fact]
    public void BundleArchiveAndRestoreReleaseAndReclaimCurrentMembers()
    {
        var firstAssetId = Guid.NewGuid();
        var secondAssetId = Guid.NewGuid();
        var bundle = AssetBundle.Create(
            "KIT-001",
            "Laptop kit",
            null,
            [
                new AssetBundleMemberSpec(
                    firstAssetId,
                    AssetBundleMemberRole.Laptop,
                    null),
                new AssetBundleMemberSpec(
                    secondAssetId,
                    AssetBundleMemberRole.Charger,
                    null),
            ],
            Now);

        Assert.All(bundle.CurrentMembers, member => Assert.True(member.IsActive));

        bundle.Archive(bundle.Version, Now.AddMinutes(1));
        Assert.All(bundle.CurrentMembers, member => Assert.False(member.IsActive));

        bundle.Restore(bundle.Version, Now.AddMinutes(2));
        Assert.All(bundle.CurrentMembers, member => Assert.True(member.IsActive));
        Assert.Null(bundle.ArchivedAtUtc);
    }

    [Fact]
    public void BundleUpdatePreservesHistoryAndValidatesOtherRole()
    {
        var firstAssetId = Guid.NewGuid();
        var secondAssetId = Guid.NewGuid();
        var bundle = AssetBundle.Create(
            "KIT-001",
            "Laptop kit",
            null,
            [
                new AssetBundleMemberSpec(
                    firstAssetId,
                    AssetBundleMemberRole.Laptop,
                    null),
            ],
            Now);

        bundle.Update(
            "KIT-001",
            "Laptop kit",
            "Ready for issue",
            [
                new AssetBundleMemberSpec(
                    secondAssetId,
                    AssetBundleMemberRole.Other,
                    "USB-C adapter"),
            ],
            bundle.Version,
            Now.AddMinutes(1));

        var current = Assert.Single(bundle.CurrentMembers);
        Assert.Equal(secondAssetId, current.AssetId);
        Assert.Equal("USB-C adapter", current.OtherRole);
        Assert.Equal(2, bundle.Members.Count);
        Assert.NotNull(bundle.Members.Single(member => member.AssetId == firstAssetId).RemovedAtUtc);

        var invalidOther = Assert.Throws<DomainRuleException>(
            () => bundle.Update(
                "KIT-CHANGED",
                "Changed name",
                null,
                [
                    new AssetBundleMemberSpec(
                        secondAssetId,
                        AssetBundleMemberRole.Other,
                        null),
                ],
                bundle.Version,
                Now.AddMinutes(2)));
        Assert.Equal("bundle_other_role_required", invalidOther.Code);
        Assert.Equal("KIT-001", bundle.Code);
        Assert.Equal("Laptop kit", bundle.Name);
        Assert.Equal(secondAssetId, Assert.Single(bundle.CurrentMembers).AssetId);
    }

    [Fact]
    public void BundleRejectsDuplicateAssetsAndStaleWrites()
    {
        var assetId = Guid.NewGuid();
        var duplicate = Assert.Throws<DomainRuleException>(
            () => AssetBundle.Create(
                "KIT-001",
                "Laptop kit",
                null,
                [
                    new AssetBundleMemberSpec(
                        assetId,
                        AssetBundleMemberRole.Laptop,
                        null),
                    new AssetBundleMemberSpec(
                        assetId,
                        AssetBundleMemberRole.Charger,
                        null),
                ],
                Now));
        Assert.Equal("bundle_duplicate_asset", duplicate.Code);

        var bundle = AssetBundle.Create(
            "KIT-002",
            "Spare kit",
            null,
            [
                new AssetBundleMemberSpec(
                    assetId,
                    AssetBundleMemberRole.Laptop,
                    null),
            ],
            Now);
        var stale = Assert.Throws<DomainRuleException>(
            () => bundle.Archive(Guid.NewGuid(), Now.AddMinutes(1)));
        Assert.Equal("concurrency_conflict", stale.Code);
    }

    [Fact]
    public void ArchivedBundleCannotBeEdited()
    {
        var assetId = Guid.NewGuid();
        var bundle = AssetBundle.Create(
            "KIT-ARCHIVE",
            "Archived kit",
            null,
            [
                new AssetBundleMemberSpec(
                    assetId,
                    AssetBundleMemberRole.Laptop,
                    null),
            ],
            Now);
        bundle.Archive(bundle.Version, Now.AddMinutes(1));

        var exception = Assert.Throws<DomainRuleException>(
            () => bundle.Update(
                "KIT-CHANGED",
                "Changed archived kit",
                null,
                [
                    new AssetBundleMemberSpec(
                        Guid.NewGuid(),
                        AssetBundleMemberRole.Charger,
                        null),
                ],
                bundle.Version,
                Now.AddMinutes(2)));

        Assert.Equal("bundle_already_archived", exception.Code);
        Assert.Equal("KIT-ARCHIVE", bundle.Code);
        Assert.Equal(assetId, Assert.Single(bundle.CurrentMembers).AssetId);
    }

    private static AssetBulkChangeJob CreateBulkJob() =>
        AssetBulkChangeJob.Create(
            AssetBulkChangeOperationKind.SetLocation,
            """{"kind":"setLocation","locationId":"11111111-1111-1111-1111-111111111111"}""",
            """[{"assetId":"22222222-2222-2222-2222-222222222222","version":"33333333-3333-3333-3333-333333333333"}]""",
            "Move equipment to ICT store",
            "fingerprint",
            1,
            Guid.NewGuid(),
            "Administrator",
            Now,
            Now.AddMinutes(30));
}
