using System.Security.Cryptography;
using Inventory.Application.Common;
using Inventory.Domain.Assets;

namespace Inventory.Application.Attachments;

public sealed record ValidatedAttachmentFile(
    string FileName,
    string ContentType,
    string Sha256);

public static class AttachmentFileValidator
{
    public static ValidatedAttachmentFile Validate(
        string fileName,
        ReadOnlySpan<byte> content)
    {
        if (content.Length is < 1 or > AssetAttachment.MaximumContentBytes)
        {
            throw Validation(
                "invalid_attachment_size",
                $"Attachment content must contain 1 to " +
                $"{AssetAttachment.MaximumContentBytes} bytes.");
        }

        var safeFileName = ValidateFileName(fileName);
        var detected = DetectFileType(content);
        var extension = Path.GetExtension(safeFileName);
        if (!detected.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw Validation(
                "attachment_extension_mismatch",
                "The file extension does not match the detected file content.");
        }

        return new ValidatedAttachmentFile(
            safeFileName,
            detected.ContentType,
            Convert.ToHexString(SHA256.HashData(content)));
    }

    private static DetectedFileType DetectFileType(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 5 &&
            content[..5].SequenceEqual("%PDF-"u8))
        {
            return new DetectedFileType("application/pdf", [".pdf"]);
        }

        if (content.Length >= 5 &&
            content[0] == 0xFF &&
            content[1] == 0xD8 &&
            content[2] == 0xFF &&
            content[^2] == 0xFF &&
            content[^1] == 0xD9)
        {
            return new DetectedFileType("image/jpeg", [".jpg", ".jpeg"]);
        }

        ReadOnlySpan<byte> pngSignature =
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (content.Length >= pngSignature.Length &&
            content[..pngSignature.Length].SequenceEqual(pngSignature))
        {
            return new DetectedFileType("image/png", [".png"]);
        }

        throw Validation(
            "unsupported_attachment_type",
            "Only PDF, JPEG, and PNG attachments are accepted.");
    }

    private static string ValidateFileName(string fileName)
    {
        var value = fileName?.Trim();
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 255 ||
            value is "." or ".." ||
            value.Any(character =>
                char.IsControl(character) ||
                "<>:\"/\\|?*".Contains(character, StringComparison.Ordinal)))
        {
            throw Validation(
                "unsafe_attachment_filename",
                "The attachment file name is missing or unsafe.");
        }

        return value;
    }

    private static RequestValidationException Validation(
        string code,
        string message) =>
        new(
            code,
            message,
            new Dictionary<string, string[]>
            {
                ["file"] = [message],
            });

    private sealed record DetectedFileType(
        string ContentType,
        IReadOnlyList<string> Extensions);
}
