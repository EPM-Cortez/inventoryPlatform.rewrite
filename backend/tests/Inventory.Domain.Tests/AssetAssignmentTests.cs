using Inventory.Domain.Assets;
using Inventory.Domain.Assignments;
using Inventory.Domain.Common;

namespace Inventory.Domain.Tests;

public sealed class AssetAssignmentTests
{
    private static readonly Guid AssetId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PersonId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ActorId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset AssignedAt =
        new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateRequiresExactlyOneTarget()
    {
        var missingTarget = Assert.Throws<DomainRuleException>(
            () => Create(personId: null, departmentId: null));
        var twoTargets = Assert.Throws<DomainRuleException>(
            () => Create(personId: PersonId, departmentId: Guid.NewGuid()));

        Assert.Equal("invalid_assignment_target", missingTarget.Code);
        Assert.Equal("invalid_assignment_target", twoTargets.Code);
    }

    [Fact]
    public void ClosePreservesCustodyPeriodAndRejectsASecondReturn()
    {
        var assignment = Create(personId: PersonId, departmentId: null);
        var originalVersion = assignment.Version;
        var returnedAt = AssignedAt.AddDays(1);

        assignment.Close(
            originalVersion,
            returnedAt,
            ActorId,
            "Technician",
            AssetCondition.Damaged,
            null,
            "Screen cracked");

        Assert.False(assignment.IsOpen);
        Assert.Equal(returnedAt, assignment.ReturnedAtUtc);
        Assert.Equal(AssetCondition.Damaged, assignment.ReturnCondition);
        Assert.Equal("Screen cracked", assignment.ReturnNotes);
        Assert.NotEqual(originalVersion, assignment.Version);

        var exception = Assert.Throws<DomainRuleException>(
            () => assignment.Close(
                assignment.Version,
                returnedAt.AddMinutes(1),
                ActorId,
                "Technician",
                AssetCondition.Damaged,
                null));
        Assert.Equal("assignment_already_closed", exception.Code);
    }

    private static AssetAssignment Create(Guid? personId, Guid? departmentId) =>
        AssetAssignment.Create(
            AssetId,
            personId,
            departmentId,
            "Target",
            "REF-1",
            AssignedAt,
            ActorId,
            "Technician",
            AssetCondition.Good,
            null);
}
