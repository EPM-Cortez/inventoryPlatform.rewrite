using Inventory.Domain.Common;
using Inventory.Domain.Importing;

namespace Inventory.Domain.Tests;

public sealed class ReferenceDataImportJobTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SuccessfulValidationRequiresVersionedCommitWithMatchingCounts()
    {
        var actorId = Guid.NewGuid();
        var job = ReferenceDataImportJob.CreateValidation(
            ReferenceDataImportResource.People,
            schemaVersion: 1,
            "staff.csv",
            new string('A', 64),
            new string('B', 64),
            ReferenceDataImportMode.CreateOnly,
            totalRows: 2,
            validRows: 2,
            errorRows: 0,
            createRows: 2,
            updateRows: 0,
            validatedRowsJson: "[]",
            errorsJson: "[]",
            Now,
            actorId,
            "Administrator");

        var version = job.Version;
        job.Commit(
            version,
            Now.AddMinutes(1),
            actorId,
            "Administrator",
            committedCreateRows: 2,
            committedUpdateRows: 0,
            committedUnchangedRows: 0);

        Assert.Equal(ReferenceDataImportStatus.Committed, job.Status);
        Assert.NotEqual(version, job.Version);
        Assert.Equal(2, job.CommittedCreateRows);
    }

    [Fact]
    public void FailedValidationCannotBeCommitted()
    {
        var actorId = Guid.NewGuid();
        var job = ReferenceDataImportJob.CreateValidation(
            ReferenceDataImportResource.Locations,
            schemaVersion: 1,
            "locations.csv",
            new string('A', 64),
            new string('B', 64),
            ReferenceDataImportMode.UpsertByKey,
            totalRows: 1,
            validRows: 0,
            errorRows: 1,
            createRows: 0,
            updateRows: 0,
            validatedRowsJson: "[]",
            errorsJson: "[{}]",
            Now,
            actorId,
            "Administrator");

        var exception = Assert.Throws<DomainRuleException>(
            () => job.Commit(
                job.Version,
                Now.AddMinutes(1),
                actorId,
                "Administrator",
                0,
                0,
                0));

        Assert.Equal("import_not_ready", exception.Code);
    }
}
