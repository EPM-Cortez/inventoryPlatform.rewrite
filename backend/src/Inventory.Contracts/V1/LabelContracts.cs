using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts.V1;

public enum LabelStockContract
{
    BrotherDk11209,
    AveryL7159,
}

public sealed class PrintAssetLabelRequest
{
    public LabelStockContract Stock { get; init; } = LabelStockContract.BrotherDk11209;

    [Range(1, 100)]
    public int Copies { get; init; } = 1;

    [Range(1, 24)]
    public int StartPosition { get; init; } = 1;

    public bool IssueMissingQr { get; init; } = true;
}

public sealed class LabelBatchItemRequest
{
    public Guid AssetId { get; init; }

    [Range(1, 100)]
    public int Quantity { get; init; } = 1;
}

public sealed class PrintLabelBatchRequest
{
    public LabelStockContract Stock { get; init; } = LabelStockContract.AveryL7159;

    [Range(1, 24)]
    public int StartPosition { get; init; } = 1;

    [MinLength(1)]
    [MaxLength(240)]
    public IReadOnlyList<LabelBatchItemRequest> Items { get; init; } = [];

    public bool IssueMissingQr { get; init; } = true;
}

public sealed record LabelStockResponse(
    LabelStockContract Stock,
    string DisplayName,
    string PageType,
    decimal LabelWidthMillimetres,
    decimal LabelHeightMillimetres,
    int LabelsPerPage,
    int Columns,
    int Rows,
    bool SupportsStartPosition);
