using System.Text;
using Inventory.Application.DataExchange;

namespace Inventory.Application.Tests;

public sealed class AssetCsvCodecTests
{
    [Fact]
    public void ImportSettingsAcceptAReadableGeneratedAssetTagPrefix()
    {
        var settings = new ImportExportSettings
        {
            GeneratedAssetTagPrefix = "STR-AUTO",
        };

        settings.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("-AUTO")]
    [InlineData("AUTO-")]
    [InlineData("AUTO TAG")]
    [InlineData("AUTO_1")]
    public void ImportSettingsRejectAnUnsafeGeneratedAssetTagPrefix(string prefix)
    {
        var settings = new ImportExportSettings
        {
            GeneratedAssetTagPrefix = prefix,
        };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);

        Assert.Contains("GeneratedAssetTagPrefix", exception.Message);
    }

    [Fact]
    public void ParserSupportsQuotedCommasEscapedQuotesAndNewlines()
    {
        var document = AssetCsvCodec.Parse(
            "assetTag,notes\r\n\"SCH,001\",\"First line\r\nSecond \"\"quoted\"\" line\"\r\n");

        Assert.Equal(2, document.Records.Count);
        Assert.Equal(["assetTag", "notes"], document.Records[0]);
        Assert.Equal("SCH,001", document.Records[1][0]);
        Assert.Equal("First line\r\nSecond \"quoted\" line", document.Records[1][1]);
    }

    [Fact]
    public void WriterEmitsUtf8BomEscapesFieldsAndNeutralizesSpreadsheetFormulas()
    {
        var content = AssetCsvCodec.Write(
            ["assetTag", "notes"],
            [
                (IReadOnlyList<string?>)["=SUM(A1:A2)", "comma, quote \" and newline\r\n"],
            ]);

        Assert.Equal([0xEF, 0xBB, 0xBF], content[..3]);
        var decoded = AssetCsvCodec.DecodeStrictUtf8(content);
        var document = AssetCsvCodec.Parse(decoded);

        Assert.Equal("'=SUM(A1:A2)", document.Records[1][0]);
        Assert.Equal(
            "=SUM(A1:A2)",
            AssetCsvCodec.RemoveFormulaProtection(document.Records[1][0]));
        Assert.Equal("comma, quote \" and newline\r\n", document.Records[1][1]);
    }

    [Fact]
    public void DecoderAndParserRejectInvalidInput()
    {
        Assert.Throws<CsvFormatException>(
            () => AssetCsvCodec.DecodeStrictUtf8([0xC3, 0x28]));
        Assert.Throws<CsvFormatException>(
            () => AssetCsvCodec.Parse("assetTag,notes\r\nA,\"unfinished"));
        Assert.Throws<CsvFormatException>(
            () => AssetCsvCodec.Parse("assetTag,notes\rA,test"));
    }

    [Fact]
    public void WriterUsesStableSchemaColumnCount()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => AssetCsvCodec.Write(
                ["one", "two"],
                [(IReadOnlyList<string?>)["only-one"]]));

        Assert.Contains("same number of fields", exception.Message);
    }
}
