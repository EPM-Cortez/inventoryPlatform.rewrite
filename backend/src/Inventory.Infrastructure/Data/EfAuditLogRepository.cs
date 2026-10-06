using Inventory.Application.Abstractions;
using Inventory.Application.Auditing;
using Inventory.Application.Common;
using Inventory.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class EfAuditLogRepository : IAuditLogRepository
{
    private readonly InventoryDbContext _dbContext;

    public EfAuditLogRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PageResult<AuditLogView>> SearchAsync(
        AuditLogSearch search,
        CancellationToken cancellationToken)
    {
        var query = ApplySearch(_dbContext.AuditLogs.AsNoTracking(), search);

        var totalCount = await query.LongCountAsync(cancellationToken);
        var storedItems = await query
            .OrderByDescending(audit => audit.OccurredAtUtc)
            .ThenByDescending(audit => audit.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(audit => new AuditLogView(
                audit.Id,
                audit.ActorUserId,
                audit.ActorDisplayName,
                audit.Action,
                audit.TargetType,
                audit.TargetId,
                audit.OccurredAtUtc,
                audit.Outcome,
                audit.DetailsJson,
                audit.IpAddress,
                audit.Client,
                audit.CorrelationId))
            .ToArrayAsync(cancellationToken);
        var items = storedItems.Select(Canonicalize).ToArray();

        return new PageResult<AuditLogView>(
            items,
            search.Page,
            search.PageSize,
            totalCount);
    }

    public async Task<IReadOnlyList<AuditLogView>> ExportAsync(
        AuditLogSearch search,
        int take,
        CancellationToken cancellationToken) =>
        (await ApplySearch(_dbContext.AuditLogs.AsNoTracking(), search)
            .OrderByDescending(audit => audit.OccurredAtUtc)
            .ThenByDescending(audit => audit.Id)
            .Take(take)
            .Select(audit => new AuditLogView(
                audit.Id,
                audit.ActorUserId,
                audit.ActorDisplayName,
                audit.Action,
                audit.TargetType,
                audit.TargetId,
                audit.OccurredAtUtc,
                audit.Outcome,
                audit.DetailsJson,
                audit.IpAddress,
                audit.Client,
                audit.CorrelationId))
            .ToArrayAsync(cancellationToken))
        .Select(Canonicalize)
        .ToArray();

    public async Task<AuditFilterVocabulary> GetFilterVocabularyAsync(
        int maximumValuesPerField,
        CancellationToken cancellationToken)
    {
        var take = checked(maximumValuesPerField + 1);
        var actions = await _dbContext.AuditLogs
            .AsNoTracking()
            .Select(audit => audit.Action)
            .Distinct()
            .OrderBy(value => value)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        var targetTypes = await _dbContext.AuditLogs
            .AsNoTracking()
            .Select(audit => audit.TargetType)
            .Distinct()
            .OrderBy(value => value)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        var outcomes = await _dbContext.AuditLogs
            .AsNoTracking()
            .Select(audit => audit.Outcome)
            .Distinct()
            .OrderBy(value => value)
            .Take(take)
            .ToArrayAsync(cancellationToken);

        return new AuditFilterVocabulary(
            CanonicalizeValues(
                actions,
                AuditVocabulary.CanonicalizeAction,
                maximumValuesPerField),
            CanonicalizeValues(
                targetTypes,
                AuditVocabulary.CanonicalizeTargetType,
                maximumValuesPerField),
            CanonicalizeValues(
                outcomes,
                AuditVocabulary.CanonicalizeOutcome,
                maximumValuesPerField),
            actions.Length > maximumValuesPerField ||
            targetTypes.Length > maximumValuesPerField ||
            outcomes.Length > maximumValuesPerField);
    }

    public void Add(Inventory.Domain.Auditing.AuditLog auditLog) =>
        _dbContext.AuditLogs.Add(auditLog);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    private static IQueryable<Inventory.Domain.Auditing.AuditLog> ApplySearch(
        IQueryable<Inventory.Domain.Auditing.AuditLog> query,
        AuditLogSearch search)
    {
        if (search.Action is not null)
        {
            var aliases =
                AuditVocabulary.GetPersistedActionAliases(search.Action);
            query = query.Where(audit => aliases.Contains(audit.Action));
        }

        if (search.TargetType is not null)
        {
            var aliases =
                AuditVocabulary.GetPersistedTargetTypeAliases(
                    search.TargetType);
            query = query.Where(audit => aliases.Contains(audit.TargetType));
        }

        if (search.TargetId is not null)
        {
            query = query.Where(audit => audit.TargetId == search.TargetId);
        }

        if (search.ActorUserId.HasValue)
        {
            query = query.Where(audit => audit.ActorUserId == search.ActorUserId.Value);
        }

        if (search.Outcome is not null)
        {
            var aliases =
                AuditVocabulary.GetPersistedOutcomeAliases(search.Outcome);
            query = query.Where(audit => aliases.Contains(audit.Outcome));
        }

        if (search.From.HasValue)
        {
            query = query.Where(audit => audit.OccurredAtUtc >= search.From.Value);
        }

        if (search.To.HasValue)
        {
            query = query.Where(audit => audit.OccurredAtUtc <= search.To.Value);
        }

        return query;
    }

    private static AuditLogView Canonicalize(AuditLogView audit) =>
        audit with
        {
            Action = AuditVocabulary.CanonicalizeAction(audit.Action),
            TargetType =
                AuditVocabulary.CanonicalizeTargetType(audit.TargetType),
            Outcome = AuditVocabulary.CanonicalizeOutcome(audit.Outcome),
        };

    private static string[] CanonicalizeValues(
        IEnumerable<string> values,
        Func<string, string> canonicalize,
        int maximumValues) =>
        values
            .Select(canonicalize)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(maximumValues)
            .ToArray();
}
