using System.Text;

namespace Inventory.Domain.Auditing;

public static class AuditVocabulary
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string ValidationFailed = "validation_failed";
    public const string Rejected = "rejected";

    private static readonly Dictionary<string, string> ActionAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["saved-search.create"] = "saved_search.created",
            ["saved-search.update"] = "saved_search.updated",
            ["saved-search.archive"] = "saved_search.archived",
            ["stocktake.asset-observed"] = "stocktake.asset.observed",
            ["leaver.create"] = "leaver.created",
            ["leaver.update"] = "leaver.updated",
            ["leaver.complete"] = "leaver.completed",
            ["leaver.cancel"] = "leaver.cancelled",
            ["attachment.scan"] = "attachment.scanned",
            ["attachment.upload"] = "attachment.uploaded",
            ["attachment.download"] = "attachment.downloaded",
            ["attachment.archive"] = "attachment.archived",
            ["assignment.expected-return.update"] =
                "assignment.expected_return.updated",
        };

    private static readonly Dictionary<string, string> TargetTypeAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["asset-label-batch"] = "asset_label_batch",
            ["assetImportJob"] = "asset_import_job",
            ["referenceDataImportJob"] = "reference_data_import_job",
            ["role-presets"] = "role_presets",
        };

    private static readonly Dictionary<string, string> OutcomeAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["success"] = Succeeded,
        };

    private static readonly Dictionary<string, string> PastTense =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["approve"] = "approved",
            ["archive"] = "archived",
            ["assign"] = "assigned",
            ["cancel"] = "cancelled",
            ["commit"] = "committed",
            ["complete"] = "completed",
            ["create"] = "created",
            ["delete"] = "deleted",
            ["dismiss"] = "dismissed",
            ["download"] = "downloaded",
            ["export"] = "exported",
            ["link"] = "linked",
            ["observe"] = "observed",
            ["restore"] = "restored",
            ["return"] = "returned",
            ["revoke"] = "revoked",
            ["scan"] = "scanned",
            ["start"] = "started",
            ["unlink"] = "unlinked",
            ["update"] = "updated",
            ["upload"] = "uploaded",
            ["validate"] = "validated",
        };

    public static string CanonicalizeAction(string value)
    {
        var trimmed = value.Trim();
        if (ActionAliases.TryGetValue(trimmed, out var aliased))
        {
            return aliased;
        }

        var normalized = NormalizeIdentifier(trimmed, preserveDots: true);
        if (normalized == "stocktake.asset_observed")
        {
            return "stocktake.asset.observed";
        }

        var lastDot = normalized.LastIndexOf('.');
        var verbStart = lastDot + 1;
        var verb = normalized[verbStart..];
        return PastTense.TryGetValue(verb, out var pastTense)
            ? string.Concat(normalized.AsSpan(0, verbStart), pastTense)
            : normalized;
    }

    public static string CanonicalizeTargetType(string value)
    {
        var trimmed = value.Trim();
        return TargetTypeAliases.TryGetValue(trimmed, out var aliased)
            ? aliased
            : NormalizeIdentifier(trimmed, preserveDots: false);
    }

    public static string CanonicalizeOutcome(string value)
    {
        var trimmed = value.Trim();
        return OutcomeAliases.TryGetValue(trimmed, out var aliased)
            ? aliased
            : NormalizeIdentifier(trimmed, preserveDots: false);
    }

    public static string[] GetPersistedActionAliases(string canonical) =>
        GetPersistedAliases(canonical, ActionAliases);

    public static string[] GetPersistedTargetTypeAliases(string canonical) =>
        GetPersistedAliases(canonical, TargetTypeAliases);

    public static string[] GetPersistedOutcomeAliases(string canonical) =>
        GetPersistedAliases(canonical, OutcomeAliases);

    private static string[] GetPersistedAliases(
        string canonical,
        IReadOnlyDictionary<string, string> aliases) =>
        aliases
            .Where(
                pair => string.Equals(
                    pair.Value,
                    canonical,
                    StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .Append(canonical)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string NormalizeIdentifier(
        string value,
        bool preserveDots)
    {
        var result = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current == '.' && preserveDots)
            {
                TrimTrailingUnderscore(result);
                if (result.Length > 0 && result[^1] != '.')
                {
                    result.Append('.');
                }

                continue;
            }

            if (current is '-' or '_' || char.IsWhiteSpace(current) ||
                (current == '.' && !preserveDots))
            {
                AppendUnderscore(result);
                continue;
            }

            if (!char.IsLetterOrDigit(current))
            {
                AppendUnderscore(result);
                continue;
            }

            if (char.IsUpper(current))
            {
                var previous = index > 0 ? value[index - 1] : '\0';
                var next = index + 1 < value.Length ? value[index + 1] : '\0';
                if (result.Length > 0 &&
                    result[^1] is not '_' and not '.' &&
                    (char.IsLower(previous) ||
                     char.IsDigit(previous) ||
                     (char.IsUpper(previous) && char.IsLower(next))))
                {
                    result.Append('_');
                }

                result.Append(char.ToLowerInvariant(current));
            }
            else
            {
                result.Append(char.ToLowerInvariant(current));
            }
        }

        while (result.Length > 0 &&
               result[^1] is '_' or '.')
        {
            result.Length--;
        }

        return result.ToString();
    }

    private static void AppendUnderscore(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] is not '_' and not '.')
        {
            builder.Append('_');
        }
    }

    private static void TrimTrailingUnderscore(StringBuilder builder)
    {
        while (builder.Length > 0 && builder[^1] == '_')
        {
            builder.Length--;
        }
    }
}
