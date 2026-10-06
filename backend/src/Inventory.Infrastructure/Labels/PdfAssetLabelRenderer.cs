using System.Collections;
using System.Globalization;
using System.Text;
using Inventory.Application.Labels;
using QRCoder;

namespace Inventory.Infrastructure.Labels;

public sealed class PdfAssetLabelRenderer : IAssetLabelPdfRenderer
{
    private const double PointsPerMillimetre = 72d / 25.4d;

    public LabelPdfDocument Render(
        LabelStock stock,
        int startPosition,
        IReadOnlyList<AssetLabelContent> labels,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (labels.Count == 0)
        {
            throw new ArgumentException("At least one label is required.", nameof(labels));
        }

        var layout = StockLayout.For(stock);
        var pages = Paginate(labels, layout, startPosition);
        var renderedPages = pages
            .Select(page => RenderPage(page, layout))
            .ToArray();
        var content = PdfDocumentWriter.Write(renderedPages);

        return new LabelPdfDocument(
            content,
            fileName,
            labels.Count,
            renderedPages.Length);
    }

    private static List<PageLabels> Paginate(
        IReadOnlyList<AssetLabelContent> labels,
        StockLayout layout,
        int startPosition)
    {
        var pages = new List<PageLabels>();
        var labelIndex = 0;
        var nextPosition = startPosition - 1;

        while (labelIndex < labels.Count)
        {
            var positioned = new List<PositionedLabel>();
            for (var position = nextPosition;
                 position < layout.LabelsPerPage && labelIndex < labels.Count;
                 position++)
            {
                positioned.Add(new PositionedLabel(position, labels[labelIndex]));
                labelIndex++;
            }

            pages.Add(new PageLabels(positioned));
            nextPosition = 0;
        }

        return pages;
    }

    private static RenderedPdfPage RenderPage(PageLabels page, StockLayout layout)
    {
        var content = new StringBuilder(capacity: 32_768);
        content.AppendLine("1 J 1 j");

        foreach (var positioned in page.Labels)
        {
            var column = positioned.Position % layout.Columns;
            var row = positioned.Position / layout.Columns;
            var labelX = layout.MarginLeftPoints + (column * layout.HorizontalPitchPoints);
            var labelY = layout.PageHeightPoints -
                         layout.MarginTopPoints -
                         ((row + 1) * layout.VerticalPitchPoints);
            RenderLabel(
                content,
                positioned.Label,
                labelX,
                labelY,
                layout.LabelWidthPoints,
                layout.LabelHeightPoints);
        }

        return new RenderedPdfPage(
            layout.PageWidthPoints,
            layout.PageHeightPoints,
            Encoding.Latin1.GetBytes(content.ToString()));
    }

    private static void RenderLabel(
        StringBuilder content,
        AssetLabelContent label,
        double x,
        double y,
        double width,
        double height)
    {
        var borderInset = Mm(0.8);
        var padding = Mm(2.2);
        var qrSize = Math.Min(Mm(24), height - (padding * 2));
        var qrX = x + width - padding - qrSize;
        var qrY = y + ((height - qrSize) / 2);
        var textX = x + padding;
        var textWidth = qrX - Mm(2) - textX;

        content.AppendLine("q");
        content
            .Append(Number(x)).Append(' ')
            .Append(Number(y)).Append(' ')
            .Append(Number(width)).Append(' ')
            .Append(Number(height)).AppendLine(" re W n");

        content.AppendLine("0.82 0.84 0.87 RG 0.35 w");
        content
            .Append(Number(x + borderInset)).Append(' ')
            .Append(Number(y + borderInset)).Append(' ')
            .Append(Number(width - (borderInset * 2))).Append(' ')
            .Append(Number(height - (borderInset * 2))).AppendLine(" re S");

        DrawText(
            content,
            "ASSET TAG",
            textX,
            y + height - Mm(4.8),
            5.2,
            bold: false,
            red: 0.34,
            green: 0.37,
            blue: 0.42);

        var assetTagSize = FitFontSize(label.AssetTag, textWidth, 10.5, 7.2);
        DrawText(
            content,
            FitSingleLine(label.AssetTag, textWidth, assetTagSize),
            textX,
            y + height - Mm(9.2),
            assetTagSize,
            bold: true);

        var deviceLines = WrapLines(label.DeviceName, textWidth, 7.1, maximumLines: 2);
        for (var index = 0; index < deviceLines.Count; index++)
        {
            DrawText(
                content,
                deviceLines[index],
                textX,
                y + height - Mm(15.2 + (index * 3.3)),
                7.1,
                bold: false,
                red: 0.12,
                green: 0.14,
                blue: 0.17);
        }

        DrawText(
            content,
            "SCAN FOR ASSET RECORD",
            textX,
            y + Mm(3.2),
            4.5,
            bold: false,
            red: 0.34,
            green: 0.37,
            blue: 0.42);

        DrawQr(content, label.QrPayloadUrl, qrX, qrY, qrSize);
        content.AppendLine("Q");
    }

    private static void DrawQr(
        StringBuilder content,
        string payload,
        double x,
        double y,
        double size)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(
            payload,
            QRCodeGenerator.ECCLevel.Q);
        var matrix = data.ModuleMatrix;
        var addedQuietZone = HasFourModuleQuietZone(matrix)
            ? 0
            : 4;
        var moduleCount = matrix.Count + (addedQuietZone * 2);
        var moduleSize = size / moduleCount;

        content.AppendLine("0 0 0 rg");
        for (var row = 0; row < matrix.Count; row++)
        {
            var modules = matrix[row];
            var column = 0;
            while (column < modules.Count)
            {
                if (!modules[column])
                {
                    column++;
                    continue;
                }

                var runStart = column;
                while (column < modules.Count && modules[column])
                {
                    column++;
                }

                var runLength = column - runStart;
                content
                    .Append(Number(x + ((addedQuietZone + runStart) * moduleSize)))
                    .Append(' ')
                    .Append(Number(
                        y +
                        ((moduleCount - addedQuietZone - row - 1) * moduleSize)))
                    .Append(' ')
                    .Append(Number(runLength * moduleSize))
                    .Append(' ')
                    .Append(Number(moduleSize + 0.01))
                    .AppendLine(" re");
            }
        }

        content.AppendLine("f");
    }

    private static bool HasFourModuleQuietZone(List<BitArray> matrix)
    {
        if (matrix.Count < 9 || matrix.Any(row => row.Count != matrix.Count))
        {
            return false;
        }

        for (var row = 0; row < matrix.Count; row++)
        {
            for (var column = 0; column < matrix.Count; column++)
            {
                var isOuterQuietZone =
                    row < 4 ||
                    column < 4 ||
                    row >= matrix.Count - 4 ||
                    column >= matrix.Count - 4;
                if (isOuterQuietZone && matrix[row][column])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void DrawText(
        StringBuilder content,
        string value,
        double x,
        double baselineY,
        double fontSize,
        bool bold,
        double red = 0,
        double green = 0,
        double blue = 0)
    {
        content
            .Append(Number(red)).Append(' ')
            .Append(Number(green)).Append(' ')
            .Append(Number(blue)).AppendLine(" rg");
        content
            .Append("BT /")
            .Append(bold ? "F2" : "F1")
            .Append(' ')
            .Append(Number(fontSize))
            .Append(" Tf 1 0 0 1 ")
            .Append(Number(x))
            .Append(' ')
            .Append(Number(baselineY))
            .Append(" Tm (")
            .Append(EscapePdfText(value))
            .AppendLine(") Tj ET");
    }

    private static List<string> WrapLines(
        string value,
        double maximumWidth,
        double fontSize,
        int maximumLines)
    {
        var remaining = SanitizeText(value).Trim();
        var lines = new List<string>(maximumLines);

        while (remaining.Length > 0 && lines.Count < maximumLines)
        {
            var isLastLine = lines.Count == maximumLines - 1;
            if (EstimateTextWidth(remaining, fontSize) <= maximumWidth)
            {
                lines.Add(remaining);
                break;
            }

            var fitLength = FindFitLength(remaining, maximumWidth, fontSize);
            if (fitLength <= 0)
            {
                break;
            }

            if (isLastLine)
            {
                lines.Add(FitSingleLine(remaining, maximumWidth, fontSize));
                break;
            }

            var breakAt = remaining.LastIndexOf(' ', Math.Min(fitLength, remaining.Length - 1));
            if (breakAt < 1)
            {
                breakAt = fitLength;
            }

            lines.Add(remaining[..breakAt].Trim());
            remaining = remaining[breakAt..].Trim();
        }

        return lines.Count == 0 ? ["Asset"] : lines;
    }

    private static string FitSingleLine(
        string value,
        double maximumWidth,
        double fontSize)
    {
        var text = SanitizeText(value).Trim();
        if (EstimateTextWidth(text, fontSize) <= maximumWidth)
        {
            return text;
        }

        const string ellipsis = "...";
        var available = maximumWidth - EstimateTextWidth(ellipsis, fontSize);
        var length = FindFitLength(text, available, fontSize);
        return length <= 0
            ? ellipsis
            : text[..length].TrimEnd() + ellipsis;
    }

    private static double FitFontSize(
        string value,
        double maximumWidth,
        double preferred,
        double minimum)
    {
        var widthAtPreferred = EstimateTextWidth(SanitizeText(value), preferred);
        return widthAtPreferred <= maximumWidth
            ? preferred
            : Math.Max(minimum, preferred * (maximumWidth / widthAtPreferred));
    }

    private static int FindFitLength(
        string value,
        double maximumWidth,
        double fontSize)
    {
        var length = 0;
        while (length < value.Length &&
               EstimateTextWidth(value[..(length + 1)], fontSize) <= maximumWidth)
        {
            length++;
        }

        return length;
    }

    private static double EstimateTextWidth(string value, double fontSize)
    {
        var units = 0d;
        foreach (var character in value)
        {
            units += character switch
            {
                ' ' => 0.28,
                'I' or 'i' or 'l' or '1' or '|' => 0.28,
                'M' or 'W' or 'm' or 'w' => 0.82,
                >= 'A' and <= 'Z' => 0.63,
                >= '0' and <= '9' => 0.56,
                _ => 0.5,
            };
        }

        return units * fontSize;
    }

    private static string EscapePdfText(string value) =>
        SanitizeText(value)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string SanitizeText(string value)
    {
        var normalized = value
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            result.Append(character <= byte.MaxValue ? character : '?');
        }

        return result.ToString().Normalize(NormalizationForm.FormC);
    }

    private static double Mm(double millimetres) => millimetres * PointsPerMillimetre;

    private static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record PositionedLabel(int Position, AssetLabelContent Label);

    private sealed record PageLabels(IReadOnlyList<PositionedLabel> Labels);

    private sealed record StockLayout(
        double PageWidthPoints,
        double PageHeightPoints,
        double LabelWidthPoints,
        double LabelHeightPoints,
        double MarginLeftPoints,
        double MarginTopPoints,
        double HorizontalPitchPoints,
        double VerticalPitchPoints,
        int Columns,
        int LabelsPerPage)
    {
        public static StockLayout For(LabelStock stock) =>
            stock switch
            {
                LabelStock.BrotherDk11209 => new(
                    Mm(62),
                    Mm(29),
                    Mm(62),
                    Mm(29),
                    0,
                    0,
                    Mm(62),
                    Mm(29),
                    1,
                    1),
                LabelStock.AveryL7159 => new(
                    Mm(210),
                    Mm(297),
                    Mm(63.5),
                    Mm(33.9),
                    Mm(7.25),
                    Mm(12.9),
                    Mm(66),
                    Mm(33.9),
                    3,
                    24),
                _ => throw new ArgumentOutOfRangeException(nameof(stock), stock, null),
            };
    }

    private sealed record RenderedPdfPage(
        double Width,
        double Height,
        byte[] Content);

    private static class PdfDocumentWriter
    {
        public static byte[] Write(IReadOnlyList<RenderedPdfPage> pages)
        {
            var objects = new Dictionary<int, byte[]>();
            objects[1] = TextObject(
                "<< /Type /Catalog /Pages 2 0 R " +
                "/ViewerPreferences << /PrintScaling /None >> >>");
            objects[3] = TextObject(
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica " +
                "/Encoding /WinAnsiEncoding >>");
            objects[4] = TextObject(
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold " +
                "/Encoding /WinAnsiEncoding >>");

            var pageObjectIds = new List<int>(pages.Count);
            for (var index = 0; index < pages.Count; index++)
            {
                var pageObjectId = 5 + (index * 2);
                var contentObjectId = pageObjectId + 1;
                var page = pages[index];
                pageObjectIds.Add(pageObjectId);
                objects[pageObjectId] = TextObject(
                    $"<< /Type /Page /Parent 2 0 R " +
                    $"/MediaBox [0 0 {Number(page.Width)} {Number(page.Height)}] " +
                    "/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> " +
                    $"/Contents {contentObjectId} 0 R >>");
                objects[contentObjectId] = StreamObject(page.Content);
            }

            objects[2] = TextObject(
                $"<< /Type /Pages /Count {pages.Count} /Kids " +
                $"[{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] >>");

            using var output = new MemoryStream();
            Write(output, "%PDF-1.4\n%");
            output.Write([0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
            var maximumObjectId = objects.Keys.Max();
            var offsets = new long[maximumObjectId + 1];

            for (var objectId = 1; objectId <= maximumObjectId; objectId++)
            {
                offsets[objectId] = output.Position;
                Write(output, $"{objectId} 0 obj\n");
                output.Write(objects[objectId]);
                Write(output, "\nendobj\n");
            }

            var xrefOffset = output.Position;
            Write(output, $"xref\n0 {maximumObjectId + 1}\n");
            Write(output, "0000000000 65535 f \n");
            for (var objectId = 1; objectId <= maximumObjectId; objectId++)
            {
                Write(output, $"{offsets[objectId]:0000000000} 00000 n \n");
            }

            Write(
                output,
                $"trailer\n<< /Size {maximumObjectId + 1} /Root 1 0 R >>\n" +
                $"startxref\n{xrefOffset}\n%%EOF\n");
            return output.ToArray();
        }

        private static byte[] TextObject(string value) =>
            Encoding.Latin1.GetBytes(value);

        private static byte[] StreamObject(byte[] content)
        {
            using var stream = new MemoryStream();
            Write(stream, $"<< /Length {content.Length} >>\nstream\n");
            stream.Write(content);
            Write(stream, "\nendstream");
            return stream.ToArray();
        }

        private static void Write(Stream stream, string value) =>
            stream.Write(Encoding.Latin1.GetBytes(value));
    }
}
