using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Application.Leavers;
using Inventory.Domain.Auditing;
using Inventory.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class EfLeaverRepository : ILeaverRepository
{
    private const int DetailAssetPreviewLimit = 100;

    private readonly InventoryDbContext _dbContext;

    public EfLeaverRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<LeaverCase?> GetAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        _dbContext.Set<LeaverCase>()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<bool> HasOpenCaseAsync(
        Guid personId,
        Guid? excludingCaseId,
        CancellationToken cancellationToken) =>
        _dbContext.Set<LeaverCase>()
            .AnyAsync(
                item =>
                    item.PersonId == personId &&
                    item.Status == LeaverCaseStatus.Open &&
                    (!excludingCaseId.HasValue || item.Id != excludingCaseId.Value),
                cancellationToken);

    public async Task<LeaverCaseDetailView?> GetViewAsync(
        Guid id,
        DateOnly schoolToday,
        CancellationToken cancellationToken)
    {
        var item = await (
            from leaverCase in _dbContext.Set<LeaverCase>().AsNoTracking()
            join person in _dbContext.People.AsNoTracking()
                on leaverCase.PersonId equals person.Id
            where leaverCase.Id == id
            select new
            {
                Case = leaverCase,
                Person = person,
            }).SingleOrDefaultAsync(cancellationToken);
        if (item is null)
        {
            return null;
        }

        var currentAssetCount = await CountCurrentAssetsAsync(
            item.Case.PersonId,
            cancellationToken);
        var assets = await ListCurrentAssetsAsync(
            item.Case.PersonId,
            schoolToday,
            skip: 0,
            DetailAssetPreviewLimit,
            cancellationToken);

        return new LeaverCaseDetailView(
            item.Case.Id,
            item.Person.Id,
            item.Person.ExternalId,
            item.Person.DisplayName,
            item.Person.IsActive,
            item.Case.LeavingOn,
            item.Case.Notes,
            (LeaverCaseStatusView)item.Case.Status,
            assets,
            currentAssetCount,
            currentAssetCount > assets.Length,
            item.Case.CreatedAtUtc,
            item.Case.CreatedByUserId,
            item.Case.CreatedByDisplayName,
            item.Case.CompletedAtUtc,
            item.Case.CompletedByUserId,
            item.Case.CompletedByDisplayName,
            item.Case.CancelledAtUtc,
            item.Case.CancelledByUserId,
            item.Case.CancelledByDisplayName,
            item.Case.Version);
    }

    public async Task<PageResult<LeaverCustodyAssetView>> GetCurrentAssetsAsync(
        Guid personId,
        DateOnly schoolToday,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var totalCount = await CountCurrentAssetsAsync(
            personId,
            cancellationToken);
        var assets = await ListCurrentAssetsAsync(
            personId,
            schoolToday,
            (page - 1) * pageSize,
            pageSize,
            cancellationToken);
        return new PageResult<LeaverCustodyAssetView>(
            assets,
            page,
            pageSize,
            totalCount);
    }

    public async Task<PageResult<LeaverCaseSummaryView>> SearchAsync(
        LeaverCaseSearch search,
        DateOnly schoolToday,
        CancellationToken cancellationToken)
    {
        var query =
            from leaverCase in _dbContext.Set<LeaverCase>().AsNoTracking()
            join person in _dbContext.People.AsNoTracking()
                on leaverCase.PersonId equals person.Id
            select new
            {
                Case = leaverCase,
                Person = person,
                CurrentAssetCount = _dbContext.AssetAssignments.Count(
                    assignment =>
                        assignment.PersonId == person.Id &&
                        assignment.ReturnedAtUtc == null),
                OverdueAssetCount = _dbContext.AssetAssignments.Count(
                    assignment =>
                        assignment.PersonId == person.Id &&
                        assignment.ReturnedAtUtc == null &&
                        assignment.ExpectedReturnOn != null &&
                        assignment.ExpectedReturnOn < schoolToday),
            };

        if (search.Status.HasValue)
        {
            var status = (LeaverCaseStatus)search.Status.Value;
            query = query.Where(row => row.Case.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search.Query))
        {
            var term = search.Query;
            query = query.Where(row =>
                row.Person.DisplayName.Contains(term) ||
                (row.Person.ExternalId != null &&
                 row.Person.ExternalId.Contains(term)));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var rows = await query
            .OrderBy(row => row.Case.Status != LeaverCaseStatus.Open)
            .ThenBy(row => row.Case.LeavingOn)
            .ThenBy(row => row.Person.DisplayName)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(row => new LeaverCaseSummaryRow(
                row.Case.Id,
                row.Person.Id,
                row.Person.ExternalId,
                row.Person.DisplayName,
                row.Person.IsActive,
                row.Case.LeavingOn,
                row.Case.Status,
                row.CurrentAssetCount,
                row.OverdueAssetCount,
                row.Case.Version))
            .ToArrayAsync(cancellationToken);
        var items = rows
            .Select(
                row => new LeaverCaseSummaryView(
                    row.Id,
                    row.PersonId,
                    row.PersonExternalId,
                    row.PersonDisplayName,
                    row.PersonIsActive,
                    row.LeavingOn,
                    (LeaverCaseStatusView)row.Status,
                    row.CurrentAssetCount,
                    row.OverdueAssetCount,
                    row.Version))
            .ToArray();
        return new PageResult<LeaverCaseSummaryView>(
            items,
            search.Page,
            search.PageSize,
            totalCount);
    }

    public async Task<PageResult<CustodyExceptionView>> SearchExceptionsAsync(
        DateOnly schoolToday,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query =
            from person in _dbContext.People.AsNoTracking()
            let currentAssetCount = _dbContext.AssetAssignments.Count(
                assignment =>
                    assignment.PersonId == person.Id &&
                    assignment.ReturnedAtUtc == null)
            let overdueAssetCount = _dbContext.AssetAssignments.Count(
                assignment =>
                    assignment.PersonId == person.Id &&
                    assignment.ReturnedAtUtc == null &&
                    assignment.ExpectedReturnOn != null &&
                    assignment.ExpectedReturnOn < schoolToday)
            let trackedCase = _dbContext.Set<LeaverCase>()
                .Where(
                    leaverCase =>
                        leaverCase.PersonId == person.Id &&
                        (leaverCase.Status == LeaverCaseStatus.Open ||
                         leaverCase.Status == LeaverCaseStatus.Completed))
                .OrderBy(leaverCase => leaverCase.Status != LeaverCaseStatus.Open)
                .ThenBy(leaverCase => leaverCase.LeavingOn)
                .Select(leaverCase => new
                {
                    leaverCase.Id,
                    leaverCase.LeavingOn,
                })
                .FirstOrDefault()
            where currentAssetCount > 0 && (!person.IsActive || trackedCase != null)
            select new
            {
                PersonId = person.Id,
                PersonExternalId = person.ExternalId,
                PersonDisplayName = person.DisplayName,
                PersonIsActive = person.IsActive,
                LeaverCaseId = trackedCase == null ? null : (Guid?)trackedCase.Id,
                LeavingOn = trackedCase == null
                    ? null
                    : (DateOnly?)trackedCase.LeavingOn,
                CurrentAssetCount = currentAssetCount,
                OverdueAssetCount = overdueAssetCount,
            };

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(item => item.PersonIsActive)
            .ThenBy(item => item.LeavingOn)
            .ThenBy(item => item.PersonDisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(
                item => new CustodyExceptionView(
                    item.PersonId,
                    item.PersonExternalId,
                    item.PersonDisplayName,
                    item.PersonIsActive,
                    item.LeaverCaseId,
                    item.LeavingOn,
                    item.CurrentAssetCount,
                    item.OverdueAssetCount))
            .ToArrayAsync(cancellationToken);
        return new PageResult<CustodyExceptionView>(
            items,
            page,
            pageSize,
            totalCount);
    }

    public void Add(LeaverCase leaverCase) =>
        _dbContext.Set<LeaverCase>().Add(leaverCase);

    public void AddAuditLog(AuditLog auditLog) =>
        _dbContext.AuditLogs.Add(auditLog);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesWithConflictMappingAsync(
                "leaver_conflict",
                "The leaver case conflicts with existing data.",
                "The leaver case was changed by another user.",
                cancellationToken);
        }
        catch (PersistenceConflictException exception)
            when (exception.GetBaseException().Message.Contains(
                "leaver_has_open_custody",
                StringComparison.Ordinal))
        {
            throw new PersistenceConflictException(
                "leaver_has_open_custody",
                "The leaver case cannot be completed while the person still has assets.",
                exception);
        }
    }

    private sealed record LeaverCaseSummaryRow(
        Guid Id,
        Guid PersonId,
        string? PersonExternalId,
        string PersonDisplayName,
        bool PersonIsActive,
        DateOnly LeavingOn,
        LeaverCaseStatus Status,
        int CurrentAssetCount,
        int OverdueAssetCount,
        Guid Version);

    private Task<int> CountCurrentAssetsAsync(
        Guid personId,
        CancellationToken cancellationToken) =>
        _dbContext.AssetAssignments.CountAsync(
            assignment =>
                assignment.PersonId == personId &&
                assignment.ReturnedAtUtc == null,
            cancellationToken);

    private async Task<LeaverCustodyAssetView[]> ListCurrentAssetsAsync(
        Guid personId,
        DateOnly schoolToday,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query =
            from assignment in _dbContext.AssetAssignments.AsNoTracking()
            join asset in _dbContext.Assets.AsNoTracking()
                on assignment.AssetId equals asset.Id
            where assignment.PersonId == personId &&
                  assignment.ReturnedAtUtc == null
            select new
            {
                Assignment = assignment,
                Asset = asset,
            };

        return await query
            .OrderBy(item => item.Asset.AssetTag)
            .ThenBy(item => item.Asset.Id)
            .Skip(skip)
            .Take(take)
            .Select(
                item => new LeaverCustodyAssetView(
                    item.Asset.Id,
                    item.Asset.AssetTag,
                    item.Asset.DeviceName,
                    item.Assignment.Id,
                    item.Assignment.AssignedAtUtc,
                    item.Assignment.ExpectedReturnOn,
                    item.Assignment.ExpectedReturnOn != null &&
                    item.Assignment.ExpectedReturnOn < schoolToday,
                    item.Assignment.Version,
                    item.Asset.Version))
            .ToArrayAsync(cancellationToken);
    }
}
