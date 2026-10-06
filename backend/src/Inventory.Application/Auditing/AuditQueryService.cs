using Inventory.Application.Abstractions;
using Inventory.Application.Common;
using Inventory.Application.DataExchange;
using Inventory.Domain.Auditing;
using System.Globalization;
using System.Text.Json;

namespace Inventory.Application.Auditing;

public sealed class AuditQueryService
{
    private readonly IAuditLogRepository _repository;
    private readonly IClock _clock;
    private readonly ICurrentUserContext _currentUser;
    private readonly AuditExportSettings _settings;

    public AuditQueryService(
        IAuditLogRepository repository,
        IClock clock,
        ICurrentUserContext currentUser,
        AuditExportSettings settings)
    {
        _repository = repository;
        _clock = clock;
        _currentUser = currentUser;
        _settings = settings;
    }

    public Task<PageResult<AuditLogView>> SearchAsync(
        AuditLogSearch search,
        CancellationToken cancellationToken)
    {
        var normalizedSearch = Normalize(search);
        return _repository.SearchAsync(normalizedSearch, cancellationToken);
    }

    public Task<AuditFilterVocabulary> GetFilterVocabularyAsync(
        CancellationToken cancellationToken) =>
        _repository.GetFilterVocabularyAsync(
            MaximumVocabularyValuesPerField,
            cancellationToken);

    public async Task<AuditExportFile> ExportAsync(
        AuditLogSearch search,
        CancellationToken cancellationToken)
    {
        var normalizedSearch = Normalize(search) with
        {
            Page = 1,
            PageSize = _settings.MaxExportRows,
        };
        var rows = await _repository.ExportAsync(
            normalizedSearch,
            checked(_settings.MaxExportRows + 1),
            cancellationToken);
        var truncated = rows.Count > _settings.MaxExportRows;
        var exportedRows = rows.Take(_settings.MaxExportRows).ToArray();
        var content = AssetCsvCodec.Write(
            [
                "id",
                "occurredAtUtc",
                "actorUserId",
                "actorDisplayName",
                "action",
                "targetType",
                "targetId",
                "outcome",
                "ipAddress",
                "client",
                "correlationId",
            ],
            exportedRows.Select(
                audit => (IReadOnlyList<string?>)
                [
                    audit.Id.ToString(),
                    audit.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
                    audit.ActorUserId?.ToString(),
                    audit.ActorDisplayName,
                    audit.Action,
                    audit.TargetType,
                    audit.TargetId,
                    audit.Outcome,
                    audit.IpAddress,
                    audit.Client,
                    audit.CorrelationId,
                ]));

        var actor = _currentUser.RequireActor();
        _repository.Add(
            AuditLog.Create(
                actor.UserId,
                actor.DisplayName,
                "audit.exported",
                "audit",
                null,
                _clock.UtcNow,
                "succeeded",
                JsonSerializer.Serialize(
                    new
                    {
                        RowCount = exportedRows.Length,
                        truncated,
                        normalizedSearch.Action,
                        normalizedSearch.TargetType,
                        normalizedSearch.TargetId,
                        normalizedSearch.ActorUserId,
                        normalizedSearch.Outcome,
                        normalizedSearch.From,
                        normalizedSearch.To,
                    }),
                actor.IpAddress,
                actor.Client,
                actor.CorrelationId));
        await _repository.SaveChangesAsync(cancellationToken);

        return new AuditExportFile(
            content,
            $"inventory-audit-{_clock.UtcNow:yyyyMMddTHHmmssZ}.csv",
            exportedRows.Length,
            truncated);
    }

    private static AuditLogSearch Normalize(AuditLogSearch search)
    {
        var page = new PageRequest(search.Page, search.PageSize).Normalize();
        if (search.From.HasValue &&
            search.To.HasValue &&
            search.To.Value < search.From.Value)
        {
            throw new RequestValidationException(
                "invalid_occurred_range",
                "to cannot precede from.",
                new Dictionary<string, string[]>
                {
                    ["to"] = ["to cannot precede from."],
                });
        }

        var normalizedSearch = search with
        {
            Action = NormalizeOptional(
                search.Action,
                AuditVocabulary.CanonicalizeAction),
            TargetType = NormalizeOptional(
                search.TargetType,
                AuditVocabulary.CanonicalizeTargetType),
            TargetId = NormalizeOptional(search.TargetId),
            Outcome = NormalizeOptional(
                search.Outcome,
                AuditVocabulary.CanonicalizeOutcome),
            Page = page.Page,
            PageSize = page.PageSize,
        };

        return normalizedSearch;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptional(
        string? value,
        Func<string, string> canonicalize) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : canonicalize(value);

    private const int MaximumVocabularyValuesPerField = 200;
}
