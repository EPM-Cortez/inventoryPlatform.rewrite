using System.Globalization;
using Inventory.Domain.Common;

namespace Inventory.Domain.AssetTags;

public sealed class AssetTagPolicy
{
    public const int SingletonId = 1;
    public const int MinimumNumericWidth = 2;
    public const int MaximumNumericWidth = 12;

    private AssetTagPolicy()
    {
    }

    private AssetTagPolicy(string prefix, int numericWidth, long nextNumber)
    {
        Id = SingletonId;
        SetValues(prefix, numericWidth, nextNumber);
        Version = Guid.NewGuid();
    }

    public int Id { get; private set; }

    public string Prefix { get; private set; } = string.Empty;

    public int NumericWidth { get; private set; }

    public long NextNumber { get; private set; }

    public Guid Version { get; private set; }

    public static AssetTagPolicy Create(
        string prefix = "AST-",
        int numericWidth = 6,
        long nextNumber = 1) =>
        new(prefix, numericWidth, nextNumber);

    public void Update(
        string prefix,
        int numericWidth,
        long nextNumber,
        Guid expectedVersion)
    {
        EnsureVersion(expectedVersion);
        SetValues(prefix, numericWidth, nextNumber);
        Version = Guid.NewGuid();
    }

    public string? PreviewNextTag() =>
        NextNumber < Pow10(NumericWidth)
            ? Format(Prefix, NumericWidth, NextNumber)
            : null;

    public static string Format(string prefix, int numericWidth, long number)
    {
        ValidateSequence(numericWidth, number);
        var normalizedPrefix = TextRules.Required(prefix, nameof(Prefix), 50);
        var formattedNumber = number.ToString(
            $"D{numericWidth}",
            CultureInfo.InvariantCulture);
        if (normalizedPrefix.Length + formattedNumber.Length > 100)
        {
            throw new DomainRuleException(
                "asset_tag_policy_too_long",
                "The generated asset tag would exceed 100 characters.");
        }

        return normalizedPrefix + formattedNumber;
    }

    private void SetValues(string prefix, int numericWidth, long nextNumber)
    {
        Prefix = TextRules.Required(prefix, nameof(Prefix), 50);
        ValidateSequence(numericWidth, nextNumber);
        _ = Format(Prefix, numericWidth, nextNumber);
        NumericWidth = numericWidth;
        NextNumber = nextNumber;
    }

    private static void ValidateSequence(int numericWidth, long number)
    {
        if (numericWidth is < MinimumNumericWidth or > MaximumNumericWidth)
        {
            throw new DomainRuleException(
                "asset_tag_numeric_width_invalid",
                $"Numeric width must be between {MinimumNumericWidth} and {MaximumNumericWidth}.");
        }

        if (number < 1)
        {
            throw new DomainRuleException(
                "asset_tag_next_number_invalid",
                "Next number must be greater than zero.");
        }

        var maximum = Pow10(numericWidth) - 1;
        if (number > maximum)
        {
            throw new DomainRuleException(
                "asset_tag_sequence_exhausted",
                "Next number does not fit within the configured numeric width.");
        }
    }

    private static long Pow10(int exponent)
    {
        var value = 1L;
        for (var index = 0; index < exponent; index++)
        {
            value *= 10;
        }

        return value;
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (expectedVersion == Guid.Empty || Version != expectedVersion)
        {
            throw new DomainRuleException(
                "concurrency_conflict",
                "The asset-tag policy was changed by another user.");
        }
    }
}
