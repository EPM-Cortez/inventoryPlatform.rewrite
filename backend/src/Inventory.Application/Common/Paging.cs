namespace Inventory.Application.Common;

public sealed record PageRequest(int Page = 1, int PageSize = 50)
{
    public const int MaximumPageSize = 200;

    public PageRequest Normalize()
    {
        if (Page < 1)
        {
            throw new RequestValidationException(
                "invalid_page",
                "Page must be at least 1.",
                new Dictionary<string, string[]> { ["page"] = ["Page must be at least 1."] });
        }

        if (PageSize is < 1 or > MaximumPageSize)
        {
            throw new RequestValidationException(
                "invalid_page_size",
                $"Page size must be between 1 and {MaximumPageSize}.",
                new Dictionary<string, string[]>
                {
                    ["pageSize"] = [$"Page size must be between 1 and {MaximumPageSize}."],
                });
        }

        var offset = (long)(Page - 1) * PageSize;
        if (offset > int.MaxValue)
        {
            throw new RequestValidationException(
                "page_offset_too_large",
                "The requested page is too far into the result set.",
                new Dictionary<string, string[]>
                {
                    ["page"] = ["The requested page is too far into the result set."],
                });
        }

        return this;
    }
}

public sealed record PageResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount);
