using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Infrastructure;

public sealed class SkiaAvatarImageProcessor : IAvatarImageProcessor
{
    private const int MaxBytes = 2_097_152;
    private const int DefaultMaxDimension = 4096;
    private const long DefaultMaxPixels = 16_777_216;
    private const int OutputSide = 256;
    private const int OutputQuality = 85;
    private const int MaxOutputBytes = 262_144;
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(5);
    private static readonly SemaphoreSlim Concurrency = new(2 * Environment.ProcessorCount);

    private readonly IAvatarCodec _codec;
    private readonly int _maxDimension;
    private readonly long _maxPixels;

    public SkiaAvatarImageProcessor()
        : this(new SkiaAvatarCodec())
    {
    }

    // Limites de producao vindos da configuracao (Avatar:MaxDimension / Avatar:MaxPixels) com o codec Skia real.
    public SkiaAvatarImageProcessor(int maxDimension, long maxPixels)
        : this(new SkiaAvatarCodec(), maxDimension, maxPixels)
    {
    }

    public SkiaAvatarImageProcessor(
        IAvatarCodec codec,
        int maxDimension = DefaultMaxDimension,
        long maxPixels = DefaultMaxPixels)
    {
        _codec = codec;
        _maxDimension = maxDimension;
        _maxPixels = maxPixels;
    }

    public async Task<AvatarProcessingResult> ProcessAsync(byte[] original, CancellationToken ct = default)
    {
        var rejection = Validate(original);
        if (rejection is not null)
        {
            return rejection;
        }

        // R-38: a espera por vaga tem o mesmo prazo do processamento; sem vaga, recusa como conteudo (422), nao fica presa.
        if (!await Concurrency.WaitAsync(ProcessingTimeout, ct))
        {
            return new AvatarRejected(AvatarRejectionReason.Content);
        }

        // O semaforo so e liberado quando o trabalho termina de fato (mesmo apos o timeout): o codec nativo nao coopera.
        var work = Task.Run(() => Reencode(original), CancellationToken.None);
        try
        {
            return await work.WaitAsync(ProcessingTimeout, ct);
        }
        catch (TimeoutException)
        {
            return new AvatarRejected(AvatarRejectionReason.Content);
        }
    }

    private AvatarProcessingResult Reencode(byte[] original)
    {
        try
        {
            if (_codec.TryReencode(original, OutputSide, OutputQuality, out var result)
                && result.Image.Length is > 0 and <= MaxOutputBytes)
            {
                return new AvatarAccepted(result.Image);
            }

            return new AvatarRejected(AvatarRejectionReason.Content);
        }
        catch (Exception)
        {
            // Inclui AccessViolationException e OutOfMemoryException vindas do codigo nativo: falha fechada.
            return new AvatarRejected(AvatarRejectionReason.Content);
        }
        finally
        {
            Concurrency.Release();
        }
    }

    private AvatarRejected? Validate(byte[] original)
    {
        if (original.Length > MaxBytes)
        {
            return new AvatarRejected(AvatarRejectionReason.Size);
        }

        if (!TryGetSignatureFormat(original, out var signatureFormat))
        {
            return new AvatarRejected(AvatarRejectionReason.Type);
        }

        if (!_codec.TryReadHeader(original)
            || !_codec.TryReadHeader(original, out var header)
            || !AvatarHeaderLimits.IsAccepted(header, signatureFormat, _maxDimension, _maxPixels))
        {
            return new AvatarRejected(AvatarRejectionReason.Content);
        }

        return null;
    }

    private static bool TryGetSignatureFormat(ReadOnlySpan<byte> data, out AvatarImageFormat format)
    {
        if (data.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            format = AvatarImageFormat.Jpeg;
        }
        else if (data.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            format = AvatarImageFormat.Png;
        }
        else if (IsWebp(data))
        {
            format = AvatarImageFormat.Webp;
        }
        else
        {
            format = default;
            return false;
        }

        return true;
    }

    private static bool IsWebp(ReadOnlySpan<byte> data)
        => data.Length >= 12
            && data[..4].SequenceEqual("RIFF"u8)
            && data.Slice(8, 4).SequenceEqual("WEBP"u8);
}
