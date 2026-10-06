using System.Globalization;

namespace Inventory.Domain.Common;

internal static class TextRules
{
    public static string Required(string? value, string fieldName, int maximumLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new DomainRuleException("required", $"{fieldName} is required.");
        }

        if (trimmed.Length > maximumLength)
        {
            throw new DomainRuleException(
                "maximum_length",
                $"{fieldName} must be {maximumLength} characters or fewer.");
        }

        return trimmed;
    }

    public static string? Optional(string? value, string fieldName, int maximumLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maximumLength)
        {
            throw new DomainRuleException(
                "maximum_length",
                $"{fieldName} must be {maximumLength} characters or fewer.");
        }

        return trimmed;
    }

    public static string NormalizeKey(string value) =>
        value.Trim().ToUpper(CultureInfo.InvariantCulture);
}
