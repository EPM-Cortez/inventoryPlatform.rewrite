using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ZXing;
using ZXing.Common;

namespace Inventory.Api.IntegrationTests;

internal static class PngQrDecoder
{
    private static readonly byte[] PngSignature =
        [137, 80, 78, 71, 13, 10, 26, 10];

    public static string Decode(byte[] png)
    {
        var image = ReadImage(png);
        var source = new RGBLuminanceSource(
            image.Rgb,
            image.Width,
            image.Height,
            RGBLuminanceSource.BitmapFormat.RGB24);
        var bitmap = new BinaryBitmap(new HybridBinarizer(source));
        var reader = new MultiFormatReader();
        var result = reader.decode(
            bitmap,
            new Dictionary<DecodeHintType, object>
            {
                [DecodeHintType.POSSIBLE_FORMATS] =
                    new[] { BarcodeFormat.QR_CODE },
                [DecodeHintType.TRY_HARDER] = true,
            });

        return result?.Text
            ?? throw new InvalidOperationException("The PNG did not decode as a QR code.");
    }

    private static RgbImage ReadImage(byte[] png)
    {
        if (png.Length < PngSignature.Length ||
            !png.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            throw new InvalidOperationException("The QR response was not a PNG.");
        }

        var position = PngSignature.Length;
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        var interlace = 0;
        byte[]? palette = null;
        using var compressed = new MemoryStream();

        while (position < png.Length)
        {
            if (position + 12 > png.Length)
            {
                throw new InvalidOperationException("The PNG chunk header was truncated.");
            }

            var length = ReadBigEndianInt32(png, position);
            position += 4;
            var type = Encoding.ASCII.GetString(png, position, 4);
            position += 4;
            if (length < 0 || position + length + 4 > png.Length)
            {
                throw new InvalidOperationException($"PNG chunk {type} was invalid.");
            }

            var data = png.AsSpan(position, length);
            switch (type)
            {
                case "IHDR":
                    if (length != 13)
                    {
                        throw new InvalidOperationException("PNG IHDR was invalid.");
                    }

                    width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                    height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                    bitDepth = data[8];
                    colorType = data[9];
                    interlace = data[12];
                    break;
                case "PLTE":
                    palette = data.ToArray();
                    break;
                case "IDAT":
                    compressed.Write(data);
                    break;
                case "IEND":
                    position = png.Length;
                    break;
            }

            position += length + 4;
        }

        if (width <= 0 || height <= 0 || interlace != 0)
        {
            throw new InvalidOperationException(
                "Only non-interlaced PNG QR images are supported by this test decoder.");
        }

        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new InvalidOperationException(
                $"PNG color type {colorType} is unsupported."),
        };
        var stride = checked((width * channels * bitDepth + 7) / 8);
        var bytesPerPixel = Math.Max(1, (channels * bitDepth + 7) / 8);
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        var filtered = raw.ToArray();
        if (filtered.Length != checked((stride + 1) * height))
        {
            throw new InvalidOperationException("PNG scanline data had an unexpected size.");
        }

        var scanlines = Unfilter(filtered, width, height, stride, bytesPerPixel);
        return new RgbImage(
            width,
            height,
            ToRgb(scanlines, width, height, stride, bitDepth, colorType, palette));
    }

    private static byte[] Unfilter(
        byte[] filtered,
        int width,
        int height,
        int stride,
        int bytesPerPixel)
    {
        _ = width;
        var output = new byte[checked(stride * height)];
        for (var row = 0; row < height; row++)
        {
            var filter = filtered[row * (stride + 1)];
            var sourceStart = row * (stride + 1) + 1;
            var targetStart = row * stride;
            for (var column = 0; column < stride; column++)
            {
                var encoded = filtered[sourceStart + column];
                var left = column >= bytesPerPixel
                    ? output[targetStart + column - bytesPerPixel]
                    : (byte)0;
                var above = row > 0
                    ? output[targetStart + column - stride]
                    : (byte)0;
                var upperLeft = row > 0 && column >= bytesPerPixel
                    ? output[targetStart + column - stride - bytesPerPixel]
                    : (byte)0;
                output[targetStart + column] = filter switch
                {
                    0 => encoded,
                    1 => unchecked((byte)(encoded + left)),
                    2 => unchecked((byte)(encoded + above)),
                    3 => unchecked((byte)(encoded + ((left + above) / 2))),
                    4 => unchecked((byte)(encoded + Paeth(left, above, upperLeft))),
                    _ => throw new InvalidOperationException(
                        $"PNG filter {filter} is unsupported."),
                };
            }
        }

        return output;
    }

    private static byte[] ToRgb(
        byte[] scanlines,
        int width,
        int height,
        int stride,
        int bitDepth,
        int colorType,
        byte[]? palette)
    {
        var rgb = new byte[checked(width * height * 3)];
        for (var row = 0; row < height; row++)
        {
            var source = scanlines.AsSpan(row * stride, stride);
            for (var column = 0; column < width; column++)
            {
                var target = (row * width + column) * 3;
                switch (colorType)
                {
                    case 0:
                        var gray = ScaleSample(
                            ReadPackedSample(source, column, bitDepth),
                            bitDepth);
                        rgb[target] = gray;
                        rgb[target + 1] = gray;
                        rgb[target + 2] = gray;
                        break;
                    case 2 when bitDepth == 8:
                        source.Slice(column * 3, 3).CopyTo(rgb.AsSpan(target, 3));
                        break;
                    case 3:
                        if (palette is null)
                        {
                            throw new InvalidOperationException(
                                "Indexed PNG had no palette.");
                        }

                        var paletteIndex = ReadPackedSample(source, column, bitDepth);
                        var paletteOffset = paletteIndex * 3;
                        if (paletteOffset + 2 >= palette.Length)
                        {
                            throw new InvalidOperationException(
                                "Indexed PNG referenced an invalid palette entry.");
                        }

                        palette.AsSpan(paletteOffset, 3).CopyTo(rgb.AsSpan(target, 3));
                        break;
                    case 4 when bitDepth == 8:
                        rgb[target] = source[column * 2];
                        rgb[target + 1] = source[column * 2];
                        rgb[target + 2] = source[column * 2];
                        break;
                    case 6 when bitDepth == 8:
                        source.Slice(column * 4, 3).CopyTo(rgb.AsSpan(target, 3));
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"PNG color type {colorType} at {bitDepth} bits is unsupported.");
                }
            }
        }

        return rgb;
    }

    private static int ReadPackedSample(
        ReadOnlySpan<byte> row,
        int sampleIndex,
        int bitDepth)
    {
        if (bitDepth == 8)
        {
            return row[sampleIndex];
        }

        if (bitDepth is not (1 or 2 or 4))
        {
            throw new InvalidOperationException(
                $"PNG bit depth {bitDepth} is unsupported.");
        }

        var samplesPerByte = 8 / bitDepth;
        var shift = (samplesPerByte - 1 - (sampleIndex % samplesPerByte)) * bitDepth;
        return (row[sampleIndex / samplesPerByte] >> shift) & ((1 << bitDepth) - 1);
    }

    private static byte ScaleSample(int sample, int bitDepth) =>
        bitDepth == 8
            ? (byte)sample
            : (byte)(sample * 255 / ((1 << bitDepth) - 1));

    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        var prediction = left + above - upperLeft;
        var leftDistance = Math.Abs(prediction - left);
        var aboveDistance = Math.Abs(prediction - above);
        var upperLeftDistance = Math.Abs(prediction - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance
            ? left
            : aboveDistance <= upperLeftDistance
                ? above
                : upperLeft;
    }

    private static int ReadBigEndianInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));

    private sealed record RgbImage(int Width, int Height, byte[] Rgb);
}
