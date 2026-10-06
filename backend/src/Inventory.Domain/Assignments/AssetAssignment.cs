using Inventory.Domain.Assets;
using Inventory.Domain.Common;

namespace Inventory.Domain.Assignments;

public enum AssignmentTargetType
{
    Person,
    Department,
}

public sealed class AssetAssignment
{
    private AssetAssignment()
    {
    }

    private AssetAssignment(
        Guid id,
        Guid assetId,
        Guid? personId,
        Guid? departmentId,
        string targetDisplayName,
        string? targetReference,
        DateTimeOffset assignedAtUtc,
        Guid assignedByUserId,
        string assignedByDisplayName,
        AssetCondition assignedCondition,
        Guid? assignedLocationId,
        string? notes,
        DateOnly? expectedReturnOn)
    {
        ValidateTarget(personId, departmentId);
        ValidateUtc(assignedAtUtc, nameof(AssignedAtUtc));
        ValidateActor(assignedByUserId);
        ValidateExpectedReturnOn(expectedReturnOn);

        Id = id;
        AssetId = assetId;
        PersonId = personId;
        DepartmentId = departmentId;
        TargetDisplayName =
            TextRules.Required(targetDisplayName, nameof(TargetDisplayName), 200);
        TargetReference =
            TextRules.Optional(targetReference, nameof(TargetReference), 200);
        AssignedAtUtc = assignedAtUtc;
        AssignedByUserId = assignedByUserId;
        AssignedByDisplayName =
            TextRules.Required(assignedByDisplayName, nameof(AssignedByDisplayName), 200);
        AssignedCondition = assignedCondition;
        AssignedLocationId = assignedLocationId;
        AssignmentNotes = TextRules.Optional(notes, nameof(AssignmentNotes), 2000);
        ExpectedReturnOn = expectedReturnOn;
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid AssetId { get; private set; }

    public Guid? PersonId { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public AssignmentTargetType TargetType =>
        PersonId.HasValue ? AssignmentTargetType.Person : AssignmentTargetType.Department;

    public string TargetDisplayName { get; private set; } = string.Empty;

    public string? TargetReference { get; private set; }

    public DateTimeOffset AssignedAtUtc { get; private set; }

    public Guid AssignedByUserId { get; private set; }

    public string AssignedByDisplayName { get; private set; } = string.Empty;

    public AssetCondition AssignedCondition { get; private set; }

    public Guid? AssignedLocationId { get; private set; }

    public string? AssignmentNotes { get; private set; }

    public DateOnly? ExpectedReturnOn { get; private set; }

    public DateTimeOffset? ReturnedAtUtc { get; private set; }

    public Guid? ReturnedByUserId { get; private set; }

    public string? ReturnedByDisplayName { get; private set; }

    public AssetCondition? ReturnCondition { get; private set; }

    public Guid? ReturnLocationId { get; private set; }

    public string? ReturnNotes { get; private set; }

    public Guid Version { get; private set; }

    public bool IsOpen => !ReturnedAtUtc.HasValue;

    public static AssetAssignment Create(
        Guid assetId,
        Guid? personId,
        Guid? departmentId,
        string targetDisplayName,
        string? targetReference,
        DateTimeOffset assignedAtUtc,
        Guid assignedByUserId,
        string assignedByDisplayName,
        AssetCondition assignedCondition,
        Guid? assignedLocationId,
        string? notes = null,
        DateOnly? expectedReturnOn = null) =>
        new(
            Guid.NewGuid(),
            assetId,
            personId,
            departmentId,
            targetDisplayName,
            targetReference,
            assignedAtUtc,
            assignedByUserId,
            assignedByDisplayName,
            assignedCondition,
            assignedLocationId,
            notes,
            expectedReturnOn);

    public void UpdateExpectedReturnOn(
        Guid expectedVersion,
        DateOnly? expectedReturnOn)
    {
        EnsureVersion(expectedVersion);
        if (!IsOpen)
        {
            throw new DomainRuleException(
                "assignment_already_closed",
                "The expected return date cannot be changed after the assignment is closed.");
        }

        ValidateExpectedReturnOn(expectedReturnOn);
        ExpectedReturnOn = expectedReturnOn;
        Version = Guid.NewGuid();
    }

    public void Close(
        Guid expectedVersion,
        DateTimeOffset returnedAtUtc,
        Guid returnedByUserId,
        string returnedByDisplayName,
        AssetCondition returnCondition,
        Guid? returnLocationId,
        string? notes = null)
    {
        EnsureVersion(expectedVersion);
        if (!IsOpen)
        {
            throw new DomainRuleException(
                "assignment_already_closed",
                "The assignment has already been returned.");
        }

        ValidateUtc(returnedAtUtc, nameof(ReturnedAtUtc));
        if (returnedAtUtc < AssignedAtUtc)
        {
            throw new DomainRuleException(
                "invalid_return_time",
                "The return time cannot be before the assignment time.");
        }

        ValidateActor(returnedByUserId);
        ReturnedAtUtc = returnedAtUtc;
        ReturnedByUserId = returnedByUserId;
        ReturnedByDisplayName =
            TextRules.Required(returnedByDisplayName, nameof(ReturnedByDisplayName), 200);
        ReturnCondition = returnCondition;
        ReturnLocationId = returnLocationId;
        ReturnNotes = TextRules.Optional(notes, nameof(ReturnNotes), 2000);
        Version = Guid.NewGuid();
    }

    private static void ValidateTarget(Guid? personId, Guid? departmentId)
    {
        var hasPerson = personId.HasValue && personId.Value != Guid.Empty;
        var hasDepartment = departmentId.HasValue && departmentId.Value != Guid.Empty;
        if (hasPerson == hasDepartment)
        {
            throw new DomainRuleException(
                "invalid_assignment_target",
                "An assignment must target exactly one person or department.");
        }
    }

    private static void ValidateExpectedReturnOn(DateOnly? expectedReturnOn)
    {
        if (expectedReturnOn == DateOnly.MinValue)
        {
            throw new DomainRuleException(
                "invalid_expected_return_date",
                "The expected return date must be a real calendar date.");
        }
    }

    private static void ValidateActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "actor_required",
                "An assignment actor is required.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value, string fieldName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                $"{fieldName} must be UTC.");
        }
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The assignment was changed by another user.");
        }
    }
}
