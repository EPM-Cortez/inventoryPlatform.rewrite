using System.Security.Cryptography;
using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Common;
using Inventory.Domain.People;

namespace Inventory.Domain.Tests;

public sealed class CustodyDocumentsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid ActorId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void AssignmentExpectedReturnIsVersionedAndCannotChangeAfterReturn()
    {
        var assignment = AssetAssignment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "A. Patel",
            "STAFF-1",
            Now,
            ActorId,
            "Technician",
            AssetCondition.Good,
            null,
            expectedReturnOn: new DateOnly(2026, 8, 1));
        var firstVersion = assignment.Version;

        assignment.UpdateExpectedReturnOn(
            firstVersion,
            new DateOnly(2026, 8, 8));

        Assert.Equal(new DateOnly(2026, 8, 8), assignment.ExpectedReturnOn);
        Assert.NotEqual(firstVersion, assignment.Version);

        assignment.Close(
            assignment.Version,
            Now.AddDays(1),
            ActorId,
            "Technician",
            AssetCondition.Good,
            null);

        var exception = Assert.Throws<DomainRuleException>(
            () => assignment.UpdateExpectedReturnOn(
                assignment.Version,
                new DateOnly(2026, 8, 9)));
        Assert.Equal("assignment_already_closed", exception.Code);
    }

    [Fact]
    public void AssignmentRejectsDefaultExpectedReturnDates()
    {
        var createException = Assert.Throws<DomainRuleException>(
            () => AssetAssignment.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                "A. Patel",
                "STAFF-1",
                Now,
                ActorId,
                "Technician",
                AssetCondition.Good,
                null,
                expectedReturnOn: DateOnly.MinValue));
        Assert.Equal("invalid_expected_return_date", createException.Code);

        var assignment = AssetAssignment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "A. Patel",
            "STAFF-1",
            Now,
            ActorId,
            "Technician",
            AssetCondition.Good,
            null);
        var updateException = Assert.Throws<DomainRuleException>(
            () => assignment.UpdateExpectedReturnOn(
                assignment.Version,
                DateOnly.MinValue));
        Assert.Equal("invalid_expected_return_date", updateException.Code);
    }

    [Fact]
    public void LeaverCaseTransitionsAreExplicitAndVersioned()
    {
        var leaverCase = LeaverCase.Create(
            Guid.NewGuid(),
            new DateOnly(2026, 8, 31),
            "Collect equipment",
            Now,
            ActorId,
            "Inventory Manager");
        var initialVersion = leaverCase.Version;

        leaverCase.Update(
            new DateOnly(2026, 9, 1),
            "Date changed",
            initialVersion);
        leaverCase.Complete(
            leaverCase.Version,
            Now.AddDays(1),
            ActorId,
            "Inventory Manager");

        Assert.Equal(LeaverCaseStatus.Completed, leaverCase.Status);
        Assert.NotNull(leaverCase.CompletedAtUtc);
        var exception = Assert.Throws<DomainRuleException>(
            () => leaverCase.Cancel(
                leaverCase.Version,
                Now.AddDays(2),
                ActorId,
                "Inventory Manager"));
        Assert.Equal("leaver_case_closed", exception.Code);

        leaverCase.MarkRehired(leaverCase.Version);
        Assert.Equal(LeaverCaseStatus.Rehired, leaverCase.Status);
        Assert.NotNull(leaverCase.CompletedAtUtc);
    }

    [Fact]
    public void AttachmentArchiveIsVersionedAndRetainsContent()
    {
        var content = "%PDF-test"u8.ToArray();
        var attachment = AssetAttachment.Create(
            Guid.NewGuid(),
            AssetAttachmentCategory.Invoice,
            "invoice.pdf",
            "application/pdf",
            content,
            Convert.ToHexString(SHA256.HashData(content)),
            Now,
            ActorId,
            "Inventory Manager");
        var version = attachment.Version;

        attachment.Archive(
            version,
            Now.AddMinutes(1),
            ActorId,
            "Inventory Manager");

        Assert.Equal(content, attachment.Content);
        Assert.NotNull(attachment.ArchivedAtUtc);
        Assert.NotEqual(version, attachment.Version);
        var exception = Assert.Throws<DomainRuleException>(
            () => attachment.Archive(
                version,
                Now.AddMinutes(2),
                ActorId,
                "Inventory Manager"));
        Assert.Equal("concurrency_conflict", exception.Code);
    }

    [Fact]
    public void SavedSearchDefaultAndArchiveMaintainInvariant()
    {
        var search = SavedAssetSearch.Create(
            ActorId,
            "Loan devices",
            SavedAssetSearch.CurrentSchemaVersion,
            "{}",
            Now);

        search.SetDefault(true, search.Version, Now.AddMinutes(1));
        Assert.True(search.IsDefault);

        search.Archive(search.Version, Now.AddMinutes(2));

        Assert.False(search.IsDefault);
        Assert.NotNull(search.ArchivedAtUtc);
        var exception = Assert.Throws<DomainRuleException>(
            () => search.Update(
                "Changed",
                SavedAssetSearch.CurrentSchemaVersion,
                "{}",
                search.Version,
                Now.AddMinutes(3)));
        Assert.Equal("saved_search_archived", exception.Code);
    }

    [Fact]
    public void SavedSearchRejectsUnknownSchemaVersion()
    {
        var exception = Assert.Throws<DomainRuleException>(
            () => SavedAssetSearch.Create(
                ActorId,
                "Unsupported",
                2,
                "{}",
                Now));

        Assert.Equal("unsupported_saved_search_schema", exception.Code);
    }
}
