using System.Text;
using Inventory.Application.Abstractions;
using QRCoder;

namespace Inventory.Infrastructure.Qr;

public sealed class QrCodeRenderer : IQrCodeRenderer
{
    public byte[] Render(string payload, QrImageFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(
            payload,
            QRCodeGenerator.ECCLevel.Q);

        return format switch
        {
            QrImageFormat.Svg => RenderSvg(data),
            QrImageFormat.Png => RenderPng(data),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
    }

    private static byte[] RenderSvg(QRCodeData data)
    {
        using var code = new SvgQRCode(data);
        return Encoding.UTF8.GetBytes(code.GetGraphic(pixelsPerModule: 8));
    }

    private static byte[] RenderPng(QRCodeData data)
    {
        using var code = new PngByteQRCode(data);
        return code.GetGraphic(
            pixelsPerModule: 12,
            drawQuietZones: true);
    }
}
