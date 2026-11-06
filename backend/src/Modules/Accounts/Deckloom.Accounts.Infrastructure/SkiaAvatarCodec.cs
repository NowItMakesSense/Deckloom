using SkiaSharp;

namespace Deckloom.Accounts.Infrastructure;

internal sealed class SkiaAvatarCodec : IAvatarCodec
{
    private const int SamplingThreshold = 512;

    public bool TryReadHeader(byte[] data)
    {
        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(data), out var result);
            return codec is not null && result == SKCodecResult.Success;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryReadHeader(byte[] data, out AvatarHeaderInfo header)
    {
        header = new AvatarHeaderInfo(0, 0, 0, AvatarImageFormat.Jpeg);
        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(data), out var result);
            if (codec is null || result != SKCodecResult.Success || !TryMapFormat(codec.EncodedFormat, out var format))
            {
                return false;
            }

            // O SKCodec trata APNG como quadro unico; o chunk acTL (antes de IDAT) revela a animacao.
            var frames = format == AvatarImageFormat.Png && HasApngAnimationControl(data)
                ? Math.Max(codec.FrameCount, 2)
                : codec.FrameCount;
            header = new AvatarHeaderInfo(codec.Info.Width, codec.Info.Height, frames, format);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryReencode(byte[] data, int outputSide, int quality, out AvatarReencodeResult result)
    {
        result = null!;
        using var codec = SKCodec.Create(new SKMemoryStream(data), out var status);
        if (codec is null || status != SKCodecResult.Success)
        {
            return false;
        }

        var size = ChooseDecodeSize(codec, outputSide);
        using var decoded = Decode(codec, size);
        if (decoded is null)
        {
            return false;
        }

        using var oriented = ApplyOrigin(decoded, codec.EncodedOrigin);
        using var encoded = EncodeCenterCrop(oriented, outputSide, quality);
        if (encoded is null)
        {
            return false;
        }

        result = new AvatarReencodeResult(encoded.ToArray(), size.Width, size.Height);
        return true;
    }

    // Acima de 512 px pede ao codec uma amostra menor, sem deixar nenhum lado abaixo da saida.
    private static SKSizeI ChooseDecodeSize(SKCodec codec, int outputSide)
    {
        var full = new SKSizeI(codec.Info.Width, codec.Info.Height);
        var shortSide = Math.Min(full.Width, full.Height);
        if (Math.Max(full.Width, full.Height) <= SamplingThreshold || shortSide <= outputSide)
        {
            return full;
        }

        var scaled = codec.GetScaledDimensions((float)outputSide / shortSide);
        return Math.Min(scaled.Width, scaled.Height) >= outputSide ? scaled : full;
    }

    private static SKBitmap? Decode(SKCodec codec, SKSizeI size)
    {
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) == SKCodecResult.Success)
        {
            return bitmap;
        }

        bitmap.Dispose();
        return null;
    }

    private static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        float w = source.Width;
        float h = source.Height;
        var swaps = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

        var target = swaps
            ? new SKBitmap(source.Height, source.Width, SKColorType.Rgba8888, SKAlphaType.Premul)
            : new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(target);
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        return target;
    }

    private static SKData? EncodeCenterCrop(SKBitmap source, int outputSide, int quality)
    {
        var side = Math.Min(source.Width, source.Height);
        var crop = SKRect.Create((source.Width - side) / 2f, (source.Height - side) / 2f, side, side);
        using var surface = SKSurface.Create(new SKImageInfo(outputSide, outputSide, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var image = SKImage.FromBitmap(source);
        surface.Canvas.DrawImage(
            image,
            crop,
            SKRect.Create(outputSide, outputSide),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var snapshot = surface.Snapshot();

        // Codifica so os pixels: o codec de saida nao recebe nem copia EXIF, XMP, GPS ou ICC.
        return snapshot.Encode(SKEncodedImageFormat.Webp, quality);
    }

    private static bool HasApngAnimationControl(ReadOnlySpan<byte> png)
    {
        const int signatureLength = 8;
        const int chunkHeaderLength = 8;
        const int chunkOverhead = 12; // tamanho + tipo + CRC
        var offset = signatureLength;
        while (offset + chunkHeaderLength <= png.Length)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
            var type = png.Slice(offset + 4, 4);
            if (type.SequenceEqual("acTL"u8))
            {
                return true;
            }

            if (type.SequenceEqual("IDAT"u8) || length > png.Length)
            {
                return false;
            }

            offset += chunkOverhead + (int)length;
        }

        return false;
    }

    private static bool TryMapFormat(SKEncodedImageFormat encoded, out AvatarImageFormat format)
    {
        switch (encoded)
        {
            case SKEncodedImageFormat.Jpeg:
                format = AvatarImageFormat.Jpeg;
                return true;
            case SKEncodedImageFormat.Png:
                format = AvatarImageFormat.Png;
                return true;
            case SKEncodedImageFormat.Webp:
                format = AvatarImageFormat.Webp;
                return true;
            default:
                format = default;
                return false;
        }
    }
}
