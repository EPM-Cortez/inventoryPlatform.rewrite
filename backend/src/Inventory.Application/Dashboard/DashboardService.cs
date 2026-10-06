using Inventory.Application.Common;
using Inventory.Application.Abstractions;

namespace Inventory.Application.Dashboard;

public sealed class DashboardService
{
    private const int DefaultCategoryLimit = 50;

    private readonly IDashboardRepository _repository;
    private readonly ISchoolSettingsProvider _schoolSettingsProvider;
    private readonly IClock _clock;

    public DashboardService(
        IDashboardRepository repository,
        ISchoolSettingsProvider schoolSettingsProvider,
        IClock clock)
    {
        _repository = repository;
        _schoolSettingsProvider = schoolSettingsProvider;
        _clock = clock;
    }

    public async Task<DashboardSummaryView> GetSummaryAsync(
        int? categoryLimit,
        CancellationToken cancellationToken)
    {
        var limit = categoryLimit ?? DefaultCategoryLimit;
        if (limit is < 1 or > PageRequest.MaximumPageSize)
        {
            throw new RequestValidationException(
                "invalid_category_limit",
                $"categoryLimit must be between 1 and {PageRequest.MaximumPageSize}.",
                new Dictionary<string, string[]>
                {
                    ["categoryLimit"] =
                    [
                        $"categoryLimit must be between 1 and {PageRequest.MaximumPageSize}.",
                    ],
                });
        }

        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        var today = SchoolBusinessDate.FromUtc(_clock.UtcNow, settings.TimeZone);
        return await _repository.GetSummaryAsync(limit, today, cancellationToken);
    }

    public Task<InventoryDistributionView> GetInventoryDistributionAsync(
        InventoryDistributionSearch search,
        CancellationToken cancellationToken)
    {
        var page = new PageRequest(search.Page, search.PageSize).Normalize();
        if (search.CategoryId == Guid.Empty)
        {
            throw new RequestValidationException(
                "invalid_category",
                "categoryId cannot be empty.",
                new Dictionary<string, string[]>
                {
                    ["categoryId"] = ["categoryId cannot be empty."],
                });
        }

        var query = NormalizeOptional(search.Query, 150, "query");
        return _repository.GetInventoryDistributionAsync(
            search with
            {
                Query = query,
                Page = page.Page,
                PageSize = page.PageSize,
            },
            cancellationToken);
    }

    public async Task<PageResult<DashboardExceptionAssetView>> GetExceptionQueueAsync(
        string exceptionCode,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!DashboardExceptionCodes.All.Contains(exceptionCode))
        {
            throw new RequestValidationException(
                "invalid_exception_code",
                "The requested dashboard exception queue is not supported.",
                new Dictionary<string, string[]>
                {
                    ["exceptionCode"] =
                    [
                        $"Use one of: {string.Join(", ", DashboardExceptionCodes.All.Order())}.",
                    ],
                });
        }

        var normalizedPage = new PageRequest(page, pageSize).Normalize();
        var settings = await _schoolSettingsProvider.GetAsync(cancellationToken);
        var today = SchoolBusinessDate.FromUtc(_clock.UtcNow, settings.TimeZone);
        return await _repository.GetExceptionQueueAsync(
            exceptionCode,
            today,
            normalizedPage.Page,
            normalizedPage.PageSize,
            cancellationToken);
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string fieldName)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maximumLength)
        {
            throw new RequestValidationException(
                "maximum_length",
                $"{fieldName} must be {maximumLength} characters or fewer.",
                new Dictionary<string, string[]>
                {
                    [fieldName] = [$"{fieldName} must be {maximumLength} characters or fewer."],
                });
        }

        return normalized;
    }
}
