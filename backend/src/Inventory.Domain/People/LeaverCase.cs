using Inventory.Domain.Common;

namespace Inventory.Domain.People;

public enum LeaverCaseStatus
{
    Open,
    Completed,
    Cancelled,
    Rehired,
}

public sealed class LeaverCase
{
    private LeaverCase()
    {
    }

    private LeaverCase(
        Guid id,
        Guid personId,
        DateOnly leavingOn,
        string? notes,
        DateTimeOffset createdAtUtc,
        Guid createdByUserId,
        string createdByDisplayName)
    {
        ValidatePerson(personId);
        ValidateLeavingOn(leavingOn);
        ValidateActor(createdByUserId);
        ValidateUtc(createdAtUtc);

        Id = id;
        PersonId = personId;
        LeavingOn = leavingOn;
        Notes = TextRules.Optional(notes, nameof(Notes), 2000);
        Status = LeaverCaseStatus.Open;
        CreatedAtUtc = createdAtUtc;
        CreatedByUserId = createdByUserId;
        CreatedByDisplayName =
            TextRules.Required(createdByDisplayName, nameof(CreatedByDisplayName), 200);
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    public DateOnly LeavingOn { get; private set; }

    public string? Notes { get; private set; }

    public LeaverCaseStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public string CreatedByDisplayName { get; private set; } = string.Empty;

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public Guid? CompletedByUserId { get; private set; }

    public string? CompletedByDisplayName { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    public string? CancelledByDisplayName { get; private set; }

    public Guid Version { get; private set; }

    public static LeaverCase Create(
        Guid personId,
        DateOnly leavingOn,
        string? notes,
        DateTimeOffset createdAtUtc,
        Guid createdByUserId,
        string createdByDisplayName) =>
        new(
            Guid.NewGuid(),
            personId,
            leavingOn,
            notes,
            createdAtUtc,
            createdByUserId,
            createdByDisplayName);

    public void Update(
        DateOnly leavingOn,
        string? notes,
        Guid expectedVersion)
    {
        EnsureOpen();
        EnsureVersion(expectedVersion);
        ValidateLeavingOn(leavingOn);
        LeavingOn = leavingOn;
        Notes = TextRules.Optional(notes, nameof(Notes), 2000);
        Version = Guid.NewGuid();
    }

    public void Complete(
        Guid expectedVersion,
        DateTimeOffset completedAtUtc,
        Guid completedByUserId,
        string completedByDisplayName)
    {
        EnsureOpen();
        EnsureVersion(expectedVersion);
        ValidateUtc(completedAtUtc);
        ValidateActor(completedByUserId);

        Status = LeaverCaseStatus.Completed;
        CompletedAtUtc = completedAtUtc;
        CompletedByUserId = completedByUserId;
        CompletedByDisplayName =
            TextRules.Required(
                completedByDisplayName,
                nameof(CompletedByDisplayName),
                200);
        Version = Guid.NewGuid();
    }

    public void Cancel(
        Guid expectedVersion,
        DateTimeOffset cancelledAtUtc,
        Guid cancelledByUserId,
        string cancelledByDisplayName)
    {
        EnsureOpen();
        EnsureVersion(expectedVersion);
        ValidateUtc(cancelledAtUtc);
        ValidateActor(cancelledByUserId);

        Status = LeaverCaseStatus.Cancelled;
        CancelledAtUtc = cancelledAtUtc;
        CancelledByUserId = cancelledByUserId;
        CancelledByDisplayName =
            TextRules.Required(
                cancelledByDisplayName,
                nameof(CancelledByDisplayName),
                200);
        Version = Guid.NewGuid();
    }

    public void MarkRehired(Guid expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status != LeaverCaseStatus.Completed)
        {
            throw new DomainRuleException(
                "leaver_case_not_completed",
                "Only a completed leaver case can be marked as rehired.");
        }

        Status = LeaverCaseStatus.Rehired;
        Version = Guid.NewGuid();
    }

    private void EnsureOpen()
    {
        if (Status != LeaverCaseStatus.Open)
        {
            throw new DomainRuleException(
                "leaver_case_closed",
                "Only an open leaver case can be changed.");
        }
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The leaver case was changed by another user.");
        }
    }

    private static void ValidatePerson(Guid personId)
    {
        if (personId == Guid.Empty)
        {
            throw new DomainRuleException(
                "person_required",
                "A leaver case must identify a person.");
        }
    }

    private static void ValidateLeavingOn(DateOnly leavingOn)
    {
        if (leavingOn == default)
        {
            throw new DomainRuleException(
                "leaving_date_required",
                "A leaver case must include a leaving date.");
        }
    }

    private static void ValidateActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "actor_required",
                "A leaver case actor is required.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                "Leaver case timestamps must be UTC.");
        }
    }
}
