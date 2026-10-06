using Inventory.Domain.Auditing;

namespace Inventory.Domain.Tests;

public sealed class AuditVocabularyTests
{
    [Theory]
    [InlineData("saved-search.create", "saved_search.created")]
    [InlineData("stocktake.asset-observed", "stocktake.asset.observed")]
    [InlineData("leaver.complete", "leaver.completed")]
    [InlineData("asset_bundle.created", "asset_bundle.created")]
    public void ActionsAreCanonicalized(string value, string expected)
    {
        Assert.Equal(expected, AuditVocabulary.CanonicalizeAction(value));
    }

    [Theory]
    [InlineData("assetImportJob", "asset_import_job")]
    [InlineData("asset-label-batch", "asset_label_batch")]
    [InlineData("stocktake_session", "stocktake_session")]
    public void TargetTypesAreCanonicalized(string value, string expected)
    {
        Assert.Equal(expected, AuditVocabulary.CanonicalizeTargetType(value));
    }

    [Fact]
    public void LegacySuccessIsCanonicalizedAndRetainedAsAFilterAlias()
    {
        Assert.Equal(
            AuditVocabulary.Succeeded,
            AuditVocabulary.CanonicalizeOutcome("success"));
        Assert.Contains(
            "success",
            AuditVocabulary.GetPersistedOutcomeAliases(
                AuditVocabulary.Succeeded));
    }
}
