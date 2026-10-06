using System.Text;

namespace Inventory.Application.DataExchange;

public static class AssetCsvSchema
{
    public static IReadOnlyList<string> Version1Headers { get; } =
    [
        "schemaVersion",
        "assetTag",
        "category",
        "manufacturer",
        "model",
        "serialNumber",
        "status",
        "condition",
        "locationCode",
        "departmentCode",
        "assignedPersonExternalId",
        "purchaseDate",
        "purchaseCost",
        "currency",
        "supplier",
        "warrantyStartDate",
        "warrantyEndDate",
        "warrantyProvider",
        "warrantyReference",
        "legacyId",
        "notes",
    ];

    public static IReadOnlyList<string> Headers { get; } =
    [
        "schemaVersion",
        "assetTag",
        "deviceName",
        "category",
        "description",
        "manufacturer",
        "model",
        "serialNumber",
        "lanMacAddress",
        "biosGuid",
        "ownership",
        "status",
        "condition",
        "locationCode",
        "departmentCode",
        "assignedPersonExternalId",
        "purchaseDate",
        "purchaseCost",
        "currency",
        "supplier",
        "warrantyStartDate",
        "warrantyEndDate",
        "warrantyProvider",
        "warrantyReference",
        "operatingSystem",
        "operatingSystemVersion",
        "windows11Compliant",
        "legacyId",
        "notes",
    ];

    public static IReadOnlyList<string> StrLegacyHeaders { get; } =
    [
        "Device Name",
        "Asset Type",
        "Description",
        "Serial Number",
        "MAC(LAN)",
        "BIOS GUID",
        "Used By",
        "School",
        "Location",
        "Product",
        "Vendor",
        "Ownership",
        "Cost",
        "Warranty",
        "Acquisition Date",
        "Warranty Expiry Date",
        "Asset State",
        "OS",
        "Os version",
        "Windows 11 Compliant",
    ];
}

public sealed record ParsedCsvDocument(IReadOnlyList<IReadOnlyList<string>> Records);

public sealed class CsvFormatException : Exception
{
    public CsvFormatException(string message)
        : base(message)
    {
    }
}

public static class AssetCsvCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly UTF8Encoding Utf8WithBom = new(
        encoderShouldEmitUTF8Identifier: true);

    public static string DecodeStrictUtf8(ReadOnlySpan<byte> content)
    {
        try
        {
            var text = StrictUtf8.GetString(content);
            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text[1..];
            }

            if (text.Contains('\0', StringComparison.Ordinal))
            {
                throw new CsvFormatException("CSV files cannot contain NUL characters.");
            }

            return text;
        }
        catch (DecoderFallbackException exception)
        {
            throw new CsvFormatException(
                $"The CSV file is not valid UTF-8: {exception.Message}");
        }
    }

    public static ParsedCsvDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var records = new List<IReadOnlyList<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var quoteClosed = false;
        var recordStarted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (character != '"')
                {
                    field.Append(character);
                    recordStarted = true;
                    continue;
                }

                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                    recordStarted = true;
                    continue;
                }

                inQuotes = false;
                quoteClosed = true;
                continue;
            }

            if (quoteClosed)
            {
                if (character == ',')
                {
                    AddField(fields, field);
                    quoteClosed = false;
                    recordStarted = true;
                    continue;
                }

                if (character is '\r' or '\n')
                {
                    AddField(fields, field);
                    AddRecord(records, fields);
                    quoteClosed = false;
                    recordStarted = false;
                    ConsumeLineFeedAfterCarriageReturn(text, ref index, character);
                    continue;
                }

                throw new CsvFormatException(
                    $"Unexpected character after a closing quote at character {index + 1}.");
            }

            switch (character)
            {
                case '"':
                    if (field.Length != 0)
                    {
                        throw new CsvFormatException(
                            $"A quote appeared inside an unquoted field at character {index + 1}.");
                    }

                    inQuotes = true;
                    recordStarted = true;
                    break;
                case ',':
                    AddField(fields, field);
                    recordStarted = true;
                    break;
                case '\r':
                case '\n':
                    AddField(fields, field);
                    AddRecord(records, fields);
                    recordStarted = false;
                    ConsumeLineFeedAfterCarriageReturn(text, ref index, character);
                    break;
                default:
                    field.Append(character);
                    recordStarted = true;
                    break;
            }
        }

        if (inQuotes)
        {
            throw new CsvFormatException("The CSV file ends inside a quoted field.");
        }

        if (quoteClosed || recordStarted || fields.Count > 0 || field.Length > 0)
        {
            AddField(fields, field);
            AddRecord(records, fields);
        }

        return new ParsedCsvDocument(records);
    }

    public static byte[] Write(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows,
        bool protectFormulas = true)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        using var output = new MemoryStream();
        using var writer = new StreamWriter(
            output,
            Utf8WithBom,
            bufferSize: 4096,
            leaveOpen: true);
        AppendRow(
            writer,
            headers.Select(header => (string?)header),
            protectFormulas: false);
        foreach (var row in rows)
        {
            if (row.Count != headers.Count)
            {
                throw new ArgumentException(
                    "Every CSV row must have the same number of fields as the header.",
                    nameof(rows));
            }

            AppendRow(writer, row, protectFormulas);
        }

        writer.Flush();
        return output.ToArray();
    }

    public static string RemoveFormulaProtection(string value)
    {
        if (value.Length < 2 || value[0] != '\'')
        {
            return value;
        }

        var firstContent = FindFirstNonWhitespace(value.AsSpan(1));
        return firstContent >= 0 && IsFormulaPrefix(value[firstContent + 1])
            ? value[1..]
            : value;
    }

    private static void AppendRow(
        TextWriter writer,
        IEnumerable<string?> values,
        bool protectFormulas)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                writer.Write(',');
            }

            first = false;
            var output = value ?? string.Empty;
            if (protectFormulas)
            {
                output = ProtectFormula(output);
            }

            if (output.IndexOfAny([',', '"', '\r', '\n']) >= 0)
            {
                writer.Write('"');
                writer.Write(output.Replace("\"", "\"\"", StringComparison.Ordinal));
                writer.Write('"');
            }
            else
            {
                writer.Write(output);
            }
        }

        writer.Write("\r\n");
    }

    private static string ProtectFormula(string value)
    {
        var firstContent = FindFirstNonWhitespace(value);
        return firstContent >= 0 && IsFormulaPrefix(value[firstContent])
            ? $"'{value}"
            : value;
    }

    private static int FindFirstNonWhitespace(ReadOnlySpan<char> value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsWhiteSpace(value[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsFormulaPrefix(char character) =>
        character is '=' or '+' or '-' or '@';

    private static void AddField(List<string> fields, StringBuilder field)
    {
        fields.Add(field.ToString());
        field.Clear();
    }

    private static void AddRecord(
        List<IReadOnlyList<string>> records,
        List<string> fields)
    {
        records.Add(fields.ToArray());
        fields.Clear();
    }

    private static void ConsumeLineFeedAfterCarriageReturn(
        string text,
        ref int index,
        char character)
    {
        if (character == '\r')
        {
            if (index + 1 >= text.Length || text[index + 1] != '\n')
            {
                throw new CsvFormatException(
                    $"A carriage return at character {index + 1} is not followed by a line feed.");
            }

            index++;
        }
    }
}
