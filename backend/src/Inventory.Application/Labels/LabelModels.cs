namespace Inventory.Application.Labels;

public enum LabelStock
{
    BrotherDk11209,
    AveryL7159,
}

public sealed record LabelStockView(
    LabelStock Stock,
    string DisplayName,
    string PageType,
    decimal LabelWidthMillimetres,
    decimal LabelHeightMillimetres,
    int LabelsPerPage,
    int Columns,
    int Rows,
    bool SupportsStartPosition);

public sealed record LabelBatchItem(
    Guid AssetId,
    int Quantity);

public sealed record PrintLabelsCommand(
    LabelStock Stock,
    int StartPosition,
    IReadOnlyList<LabelBatchItem> Items,
    bool IssueMissingQr);

public sealed record AssetLabelContent(
    Guid AssetId,
    string AssetTag,
    string DeviceName,
    string QrPayloadUrl);

public sealed record LabelPdfDocument(
    byte[] Content,
    string FileName,
    int LabelCount,
    int PageCount);

public interface IAssetLabelPdfRenderer
{
    LabelPdfDocument Render(
        LabelStock stock,
        int startPosition,
        IReadOnlyList<AssetLabelContent> labels,
        string fileName);
}

public static class LabelStockCatalog
{
    private static readonly IReadOnlyList<LabelStockView> Stocks =
    [
        new(
            LabelStock.BrotherDk11209,
            "Brother DK-11209 (62 x 29 mm)",
            "continuousRoll",
            62m,
            29m,
            1,
            1,
            1,
            false),
        new(
            LabelStock.AveryL7159,
            "Avery L7159 (A4, 24 labels)",
            "a4Sheet",
            63.5m,
            33.9m,
            24,
            3,
            8,
            true),
    ];

    public static IReadOnlyList<LabelStockView> List() => Stocks;

    public static LabelStockView Get(LabelStock stock) =>
        Stocks.SingleOrDefault(candidate => candidate.Stock == stock)
        ?? throw new ArgumentOutOfRangeException(nameof(stock), stock, null);
}
