using Inventory.Application.Common;

namespace Inventory.Application.SavedSearches;

public static class SavedAssetFilterValidator
{
    public static SavedAssetFilter Validate(SavedAssetFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var normalized = filter with
        {
            Query = NormalizeOptional(filter.Query, 200, "filter.query"),
            AssetTag = NormalizeOptional(filter.AssetTag, 100, "filter.assetTag"),
            SerialNumber = NormalizeOptional(
                filter.SerialNumber,
                200,
                "filter.serialNumber"),
            LegacyId = NormalizeOptional(filter.LegacyId, 200, "filter.legacyId"),
            Sort = ValidateSort(filter.Sort),
        };

        ValidateIdentifier(normalized.CategoryId, "filter.categoryId");
        ValidateIdentifier(normalized.LocationId, "filter.locationId");
        ValidateIdentifier(normalized.DepartmentId, "filter.departmentId");
        ValidateIdentifier(normalized.AssignedPersonId, "filter.assignedPersonId");
        ValidateEnum(normalized.Status, "filter.status");
        ValidateEnum(normalized.Condition, "filter.condition");
        ValidateEnum(normalized.WarrantyState, "filter.warrantyState");

        if (normalized.DepartmentId.HasValue &&
            normalized.AssignedPersonId.HasValue)
        {
            throw Validation(
                "A saved search cannot filter by a person and department together.",
                "filter");
        }

        if (normalized.Assigned == false &&
            (normalized.DepartmentId.HasValue ||
             normalized.AssignedPersonId.HasValue))
        {
            throw Validation(
                "An unassigned filter cannot also identify a holder.",
                "filter.assigned");
        }

        if ((normalized.CreatedFrom.HasValue &&
             normalized.CreatedFrom.Value.Offset != TimeSpan.Zero) ||
            (normalized.CreatedTo.HasValue &&
             normalized.CreatedTo.Value.Offset != TimeSpan.Zero))
        {
            throw Validation(
                "Created date-time filters must be UTC.",
                "filter");
        }

        if (normalized.CreatedFrom > normalized.CreatedTo)
        {
            throw Validation(
                "CreatedFrom cannot be after CreatedTo.",
                "filter");
        }

        if (normalized.PageSize is < 1 or > PageRequest.MaximumPageSize)
        {
            throw Validation(
                $"PageSize must be between 1 and {PageRequest.MaximumPageSize}.",
                "filter.pageSize");
        }

        return normalized;
    }

    private static string ValidateSort(string? sort)
    {
        var normalized = string.IsNullOrWhiteSpace(sort) ? "assetTag" : sort.Trim();
        return normalized switch
        {
            "assetTag" or "-assetTag" or
            "updatedAtUtc" or "-updatedAtUtc" or
            "status" or "-status" => normalized,
            _ => throw Validation(
                "Sort must be assetTag, updatedAtUtc, status, or a descending '-' form.",
                "filter.sort"),
        };
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string field)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maximumLength)
        {
            throw Validation(
                $"{field} cannot exceed {maximumLength} characters.",
                field);
        }

        return normalized;
    }

    private static void ValidateIdentifier(Guid? id, string field)
    {
        if (id == Guid.Empty)
        {
            throw Validation(
                $"{field} cannot be an empty identifier.",
                field);
        }
    }

    private static void ValidateEnum<TEnum>(TEnum? value, string field)
        where TEnum : struct, Enum
    {
        if (value.HasValue && !Enum.IsDefined(value.Value))
        {
            throw Validation($"{field} is not supported.", field);
        }
    }

    private static RequestValidationException Validation(
        string message,
        string field) =>
        new(
            "invalid_saved_search_filter",
            message,
            new Dictionary<string, string[]>
            {
                [field] = [message],
            });
}
