using Inventory.Domain.Common;
using Inventory.Domain.People;

namespace Inventory.Domain.Tests;

public sealed class PeopleTests
{
    [Fact]
    public void PersonNormalizesExternalIdAndValidatesEmail()
    {
        var person = Person.Create(
            "  stf-1042 ",
            "  A. Patel ",
            PersonType.Staff,
            "a.patel@example.test");

        Assert.Equal("stf-1042", person.ExternalId);
        Assert.Equal("STF-1042", person.ExternalIdNormalized);
        Assert.Equal("A. Patel", person.DisplayName);
        Assert.True(person.IsActive);
        Assert.NotEqual(Guid.Empty, person.Version);

        var exception = Assert.Throws<DomainRuleException>(
            () => Person.Create(null, "Invalid Email", PersonType.Other, "not-an-email"));
        Assert.Equal("invalid_email", exception.Code);
    }

    [Fact]
    public void PersonAndDepartmentRejectStaleUpdates()
    {
        var person = Person.Create("P-1", "Person", PersonType.Pupil);
        var department = Department.Create("ICT", "ICT Services");

        var personException = Assert.Throws<DomainRuleException>(
            () => person.Update(
                "P-1",
                "Changed",
                PersonType.Pupil,
                null,
                true,
                Guid.NewGuid()));
        var departmentException = Assert.Throws<DomainRuleException>(
            () => department.Update("ICT", "Changed", true, Guid.NewGuid()));

        Assert.Equal("concurrency_conflict", personException.Code);
        Assert.Equal("concurrency_conflict", departmentException.Code);
    }
}
