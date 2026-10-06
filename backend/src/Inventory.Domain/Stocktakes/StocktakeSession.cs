using Inventory.Domain.Common;

namespace Inventory.Domain.Stocktakes;

public sealed class StocktakeSession
{
    private StocktakeSession()
    {
    }

    private StocktakeSession(
        Guid id,
        string name,
        StocktakeScope scope,
        Guid? scopeLocationId,
        Guid? scopeCategoryId,
        DateTimeOffset startedAtUtc,
        Guid startedByUserId,
        string startedByDisplayName)
    {
        ValidateUtc(startedAtUtc);
        ValidateScope(scope, scopeLocationId, scopeCategoryId);
        if (startedByUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "actor_required",
                "A stocktake must record who started it.");
        }

        Id = id;
        Name = TextRules.Required(name, nameof(Name), 200);
        Scope = scope;
        ScopeLocationId = scopeLocationId;
        ScopeCategoryId = scopeCategoryId;
        Status = StocktakeStatus.InProgress;
        StartedAtUtc = startedAtUtc;
        StartedByUserId = startedByUserId;
        StartedByDisplayName = TextRules.Required(
            startedByDisplayName,
            nameof(StartedByDisplayName),
            200);
        Version = Guid.NewGuid();
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public StocktakeScope Scope { get; private set; }

    public Guid? ScopeLocationId { get; private set; }

    public Guid? ScopeCategoryId { get; private set; }

    public StocktakeStatus Status { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public Guid StartedByUserId { get; private set; }

    public string StartedByDisplayName { get; private set; } = string.Empty;

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public Guid? CompletedByUserId { get; private set; }

    public string? CompletedByDisplayName { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    public string? CancelledByDisplayName { get; private set; }

    public string? CancellationReason { get; private set; }

    public Guid Version { get; private set; }

    public static StocktakeSession Start(
        string name,
        StocktakeScope scope,
        Guid? scopeLocationId,
        Guid? scopeCategoryId,
        DateTimeOffset startedAtUtc,
        Guid startedByUserId,
        string startedByDisplayName) =>
        new(
            Guid.NewGuid(),
            name,
            scope,
            scopeLocationId,
            scopeCategoryId,
            startedAtUtc,
            startedByUserId,
            startedByDisplayName);

    /// <summary>
    /// Registers a scan against the session. An unchanged rescan is deliberately
    /// accepted before the version check so a retried request is idempotent.
    /// </summary>
    public bool RegisterObservation(
        Guid expectedVersion,
        DateTimeOffset observedAtUtc,
        bool observationChanged)
    {
        EnsureInProgress();
        if (!observationChanged)
        {
            return false;
        }

        EnsureVersion(expectedVersion);
        ValidateUtc(observedAtUtc);
        Version = Guid.NewGuid();
        return true;
    }

    public void Complete(
        Guid expectedVersion,
        DateTimeOffset completedAtUtc,
        Guid completedByUserId,
        string completedByDisplayName)
    {
        EnsureInProgress();
        EnsureVersion(expectedVersion);
        ValidateUtc(completedAtUtc);
        ValidateActor(completedByUserId, completedByDisplayName);

        Status = StocktakeStatus.Completed;
        CompletedAtUtc = completedAtUtc;
        CompletedByUserId = completedByUserId;
        CompletedByDisplayName = TextRules.Required(
            completedByDisplayName,
            nameof(CompletedByDisplayName),
            200);
        Version = Guid.NewGuid();
    }

    public void Cancel(
        string reason,
        Guid expectedVersion,
        DateTimeOffset cancelledAtUtc,
        Guid cancelledByUserId,
        string cancelledByDisplayName)
    {
        EnsureInProgress();
        EnsureVersion(expectedVersion);
        ValidateUtc(cancelledAtUtc);
        ValidateActor(cancelledByUserId, cancelledByDisplayName);

        Status = StocktakeStatus.Cancelled;
        CancelledAtUtc = cancelledAtUtc;
        CancelledByUserId = cancelledByUserId;
        CancelledByDisplayName = TextRules.Required(
            cancelledByDisplayName,
            nameof(CancelledByDisplayName),
            200);
        CancellationReason = TextRules.Required(reason, nameof(CancellationReason), 1000);
        Version = Guid.NewGuid();
    }

    private static void ValidateScope(
        StocktakeScope scope,
        Guid? scopeLocationId,
        Guid? scopeCategoryId)
    {
        if (!Enum.IsDefined(scope))
        {
            throw new DomainRuleException(
                "invalid_stocktake_scope",
                "The stocktake scope is not supported.");
        }

        var valid = scope switch
        {
            StocktakeScope.All =>
                !scopeLocationId.HasValue && !scopeCategoryId.HasValue,
            StocktakeScope.Location =>
                scopeLocationId is { } locationId &&
                locationId != Guid.Empty &&
                !scopeCategoryId.HasValue,
            StocktakeScope.Category =>
                scopeCategoryId is { } categoryId &&
                categoryId != Guid.Empty &&
                !scopeLocationId.HasValue,
            _ => false,
        };
        if (!valid)
        {
            throw new DomainRuleException(
                "invalid_stocktake_scope",
                "Supply only the identifier required by the selected stocktake scope.");
        }
    }

    private static void ValidateActor(Guid actorUserId, string actorDisplayName)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new DomainRuleException(
                "actor_required",
                "A stocktake action must record who performed it.");
        }

        _ = TextRules.Required(actorDisplayName, nameof(actorDisplayName), 200);
    }

    private void EnsureInProgress()
    {
        if (Status != StocktakeStatus.InProgress)
        {
            throw new DomainRuleException(
                "stocktake_not_in_progress",
                "Only an in-progress stocktake can be changed.");
        }
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The stocktake was changed by another user.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException(
                "utc_required",
                "Stocktake timestamps must be UTC.");
        }
    }
}
