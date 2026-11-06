using System.Diagnostics;
using System.Text;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Infrastructure;
using SkiaSharp;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-072 (REQ-019; ADR-0011 passos 4, 5 e 7): decodificacao limitada, recorte central 256x256 e reencode WebP q85
// a partir so dos pixels, sob semaforo (2 por nucleo) e timeout de 5 s.
//
// Costura exigida do GREEN (declarada no RED para compilar):
//  - `IAvatarCodec.TryReencode(byte[] data, int outputSide, int quality, out AvatarReencodeResult result)`:
//    decodifica (amostragem reduzida do codec quando o original passa de 512 px, orientacao EXIF aplicada aos pixels),
//    recorta ao centro, redimensiona para outputSide x outputSide e codifica WebP sem metadados.
//    `AvatarReencodeResult(Image, DecodedWidth, DecodedHeight)` informa o tamanho REAL da amostra decodificada.
//  - `SkiaAvatarCodec` passa a ser publico (o teste de amostragem embrulha o codec real num gravador).
//  - `SkiaAvatarImageProcessor` chama TryReencode apos o cabecalho aceito, sob `SemaphoreSlim(2 * nucleos)` e timeout de 5 s;
//    excecao do codec, timeout ou saida > 256 KB viram `AvatarRejected(Content)` sem propagar.
//
// Imagens geradas em memoria com o proprio SkiaSharp (nada de binario grande no repositorio). O JPEG de ~1,5 MB recebe,
// injetados a mao apos o APP0: APP1 Exif (TIFF big-endian: Orientation=6 e ponteiro para IFD GPS com GPSLatitudeRef),
// APP1 XMP, APP2 ICC_PROFILE (perfil sintetico, nao e um ICC valido) e segmentos COM com bytes pseudoaleatorios
// ate ~1,5 MB (o tamanho vem do preenchimento, nao de ruido nos pixels, para manter as cores verificaveis).
[Collection("AvatarProcessorSemaphore")]
public class AvatarReencodeTests
{
    private const int Side = 256;
    private const int MaxOutputBytes = 262_144;

    static AvatarReencodeTests()
    {
        // Os testes de semaforo bloqueiam dezenas de threads de proposito; evita a injecao lenta do pool.
        ThreadPool.SetMinThreads(512, 512);
    }

    // ---- JPEG com EXIF/GPS/orientacao 6, ~1,5 MB ----

    [Fact]
    public async Task Jpeg_with_exif_gps_and_orientation_6_becomes_256x256_webp_REQ_019()
    {
        var jpeg = BuildJpegWithMetadata();
        Assert.InRange(jpeg.Length, 1_400_000, 1_700_000);

        var result = await new SkiaAvatarImageProcessor().ProcessAsync(jpeg);

        var output = AssertAccepted(result);
        AssertWebp256(output);
    }

    [Fact]
    public async Task Jpeg_orientation_6_is_applied_to_the_pixels_REQ_019()
    {
        // armazenado 1600x1200: esquerda vermelha, direita azul. Orientacao 6 gira 90 graus horario na exibicao:
        // a esquerda armazenada vira o TOPO. Recorte central 1200x1200 mantem as metades.
        var result = await new SkiaAvatarImageProcessor().ProcessAsync(BuildJpegWithMetadata());

        using var bitmap = SKBitmap.Decode(AssertAccepted(result));
        Assert.True(IsRed(bitmap.GetPixel(128, 16)), "topo deveria ser vermelho");
        Assert.True(IsBlue(bitmap.GetPixel(128, 240)), "base deveria ser azul");
    }

    [Fact]
    public async Task Jpeg_output_has_no_exif_xmp_gps_or_icc_REQ_019()
    {
        var jpeg = BuildJpegWithMetadata();
        // sanidade: a entrada realmente carrega os metadados que a saida nao pode ter.
        Assert.True(Contains(jpeg, "Exif\0\0"u8), "entrada sem Exif");
        Assert.True(Contains(jpeg, "http://ns.adobe.com/xap/1.0/"u8), "entrada sem XMP");
        Assert.True(Contains(jpeg, "ICC_PROFILE"u8), "entrada sem ICC");

        var output = AssertAccepted(await new SkiaAvatarImageProcessor().ProcessAsync(jpeg));

        AssertNoMetadata(output);
    }

    // ---- PNG e WebP validos ----

    [Fact]
    public async Task Valid_png_is_reencoded_to_256x256_webp_REQ_019()
    {
        var output = AssertAccepted(await new SkiaAvatarImageProcessor().ProcessAsync(Encode(SKEncodedImageFormat.Png, 100)));

        AssertWebp256(output);
        AssertNoMetadata(output);
        AssertLeftRightHalves(output);
    }

    [Fact]
    public async Task Valid_webp_is_reencoded_to_256x256_webp_REQ_019()
    {
        var output = AssertAccepted(await new SkiaAvatarImageProcessor().ProcessAsync(Encode(SKEncodedImageFormat.Webp, 90)));

        AssertWebp256(output);
        AssertNoMetadata(output);
        AssertLeftRightHalves(output);
    }

    [Fact]
    public async Task Output_is_at_most_256_KB_even_for_a_noisy_image_REQ_019()
    {
        // 700x700 de ruido cabe no limite de entrada (2 MiB), entao chega ao reencode.
        var input = NoisyPng(700, 700);
        Assert.InRange(input.Length, 1, 2 * 1024 * 1024);

        var output = AssertAccepted(await new SkiaAvatarImageProcessor().ProcessAsync(input));

        Assert.InRange(output.Length, 1, MaxOutputBytes);
    }

    [Fact]
    public async Task Codec_output_above_256_KB_is_refused_as_content_REQ_019()
    {
        var codec = new ScriptedCodec(_ => new AvatarReencodeResult(new byte[MaxOutputBytes + 1], 256, 256));

        var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(Fixture("pixel.png"));

        AssertContentRejected(result);
    }

    [Fact]
    public async Task Processor_asks_the_codec_for_256_pixels_at_quality_85_and_returns_its_bytes_REQ_019()
    {
        var expected = new byte[] { 1, 2, 3, 4 };
        var codec = new ScriptedCodec(_ => new AvatarReencodeResult(expected, 256, 256));

        var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(Fixture("pixel.png"));

        Assert.Equal(expected, AssertAccepted(result));
        Assert.Equal(1, codec.Reencodes);
        Assert.Equal(256, codec.LastOutputSide);
        Assert.Equal(85, codec.LastQuality);
    }

    // ---- amostragem reduzida (> 512 px) ----

    [Fact]
    public async Task Original_above_512px_is_decoded_with_reduced_sampling_REQ_019()
    {
        var recorder = new RecordingCodec(new SkiaAvatarCodec());

        var result = await new SkiaAvatarImageProcessor(recorder).ProcessAsync(EncodeSolid(2048, 2048, SKEncodedImageFormat.Jpeg, 90));

        AssertWebp256(AssertAccepted(result));
        Assert.NotNull(recorder.Last);
        Assert.True(recorder.Last!.DecodedWidth < 2048 && recorder.Last.DecodedHeight < 2048, "amostra deveria ser menor que o original");
        Assert.True(recorder.Last.DecodedWidth >= Side && recorder.Last.DecodedHeight >= Side, "amostra nao pode ficar abaixo da saida");
    }

    [Fact]
    public async Task Original_up_to_512px_is_decoded_at_full_size_REQ_019()
    {
        var recorder = new RecordingCodec(new SkiaAvatarCodec());

        var result = await new SkiaAvatarImageProcessor(recorder).ProcessAsync(EncodeSolid(400, 300, SKEncodedImageFormat.Jpeg, 90));

        AssertWebp256(AssertAccepted(result));
        Assert.Equal(400, recorder.Last!.DecodedWidth);
        Assert.Equal(300, recorder.Last.DecodedHeight);
    }

    // ---- semaforo (2 por nucleo) ----

    [Fact]
    public async Task At_most_two_processings_per_core_run_at_the_same_time_REQ_019()
    {
        var limit = 2 * Environment.ProcessorCount;
        var total = limit + 4;
        using var gate = new ManualResetEventSlim(false);
        var codec = new BlockingCodec(gate);
        var processor = new SkiaAvatarImageProcessor(codec);
        var body = Fixture("pixel.png");

        var tasks = Enumerable.Range(0, total)
            .Select(_ => Task.Factory.StartNew(() => processor.ProcessAsync(body), TaskCreationOptions.LongRunning).Unwrap())
            .ToArray();

        try
        {
            var deadline = Stopwatch.StartNew();
            while (codec.Current < limit && deadline.Elapsed < TimeSpan.FromSeconds(3))
            {
                await Task.Delay(10);
            }

            await Task.Delay(300); // folga para uma eventual violacao do limite aparecer
            Assert.Equal(limit, codec.MaxObserved);
        }
        finally
        {
            gate.Set();
        }

        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.All(results, r => Assert.IsType<AvatarAccepted>(r));
        Assert.Equal(total, codec.Completed);
        Assert.Equal(limit, codec.MaxObserved);
    }

    // ---- timeout de 5 s ----

    [Fact]
    public async Task Processing_longer_than_5_seconds_is_cancelled_as_content_REQ_019()
    {
        using var gate = new ManualResetEventSlim(false); // o codec nativo nao coopera: ignora cancelamento
        var codec = new BlockingCodec(gate);
        var clock = Stopwatch.StartNew();

        try
        {
            var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(Fixture("pixel.png")).WaitAsync(TimeSpan.FromSeconds(9));

            clock.Stop();
            AssertContentRejected(result);
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(7));
        }
        finally
        {
            gate.Set();
        }
    }

    // R-38: sem vaga no semaforo, a espera tem prazo e termina em 422 content (nao espera para sempre).
    [Fact]
    public async Task Without_a_free_slot_the_wait_is_bounded_and_ends_as_content_R38()
    {
        var limit = 2 * Environment.ProcessorCount;
        using var gate = new ManualResetEventSlim(false); // codec travado: as vagas nao voltam
        var codec = new BlockingCodec(gate);
        var processor = new SkiaAvatarImageProcessor(codec);
        var body = Fixture("pixel.png");

        var holders = Enumerable.Range(0, limit)
            .Select(_ => Task.Factory.StartNew(() => processor.ProcessAsync(body), TaskCreationOptions.LongRunning).Unwrap())
            .ToArray();

        try
        {
            var deadline = Stopwatch.StartNew();
            while (codec.Current < limit && deadline.Elapsed < TimeSpan.FromSeconds(3))
            {
                await Task.Delay(10);
            }

            Assert.Equal(limit, codec.Current);
            var clock = Stopwatch.StartNew();

            var result = await processor.ProcessAsync(body).WaitAsync(TimeSpan.FromSeconds(9));

            clock.Stop();
            AssertContentRejected(result);
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(7));
        }
        finally
        {
            gate.Set();
        }

        await Task.WhenAll(holders).WaitAsync(TimeSpan.FromSeconds(15));
    }

    // ---- excecao do codec nativo ----

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(AccessViolationException))]
    [InlineData(typeof(OutOfMemoryException))]
    public async Task Codec_exception_becomes_content_without_propagating_REQ_019(Type exceptionType)
    {
        var codec = new ScriptedCodec(_ => throw (Exception)Activator.CreateInstance(exceptionType)!);

        var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(Fixture("pixel.png"));

        AssertContentRejected(result);
        Assert.Equal(1, codec.Reencodes);
    }

    [Fact]
    public async Task Codec_returning_false_becomes_content_REQ_019()
    {
        var codec = new ScriptedCodec(_ => null);

        var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(Fixture("pixel.png"));

        AssertContentRejected(result);
    }

    // ---- asserts ----

    private static byte[] AssertAccepted(AvatarProcessingResult result) => Assert.IsType<AvatarAccepted>(result).Image;

    private static void AssertContentRejected(AvatarProcessingResult result)
        => Assert.Equal(AvatarRejectionReason.Content, Assert.IsType<AvatarRejected>(result).Reason);

    private static void AssertWebp256(byte[] output)
    {
        Assert.InRange(output.Length, 1, MaxOutputBytes);
        Assert.True(output.AsSpan(0, 4).SequenceEqual("RIFF"u8) && output.AsSpan(8, 4).SequenceEqual("WEBP"u8), "nao e RIFF/WEBP");
        using var codec = SKCodec.Create(new SKMemoryStream(output));
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        Assert.Equal(Side, codec.Info.Width);
        Assert.Equal(Side, codec.Info.Height);
        // O Skia devolve FrameCount 0 para WebP estatico (VP8 e VP8L); animado teria > 1.
        Assert.InRange(codec.FrameCount, 0, 1);
    }

    private static void AssertNoMetadata(byte[] output)
    {
        // WebP so carrega metadados em VP8X + chunks EXIF/XMP /ICCP; o chunk de imagem vem direto (VP8 ou VP8L).
        var firstChunk = Encoding.ASCII.GetString(output, 12, 4);
        Assert.True(firstChunk is "VP8 " or "VP8L", $"primeiro chunk inesperado: {firstChunk}");

        foreach (var signature in new[] { "EXIF", "XMP ", "ICCP", "VP8X", "Exif", "ns.adobe.com", "xmpmeta", "ICC_PROFILE", "GPS" })
        {
            Assert.False(Contains(output, Encoding.ASCII.GetBytes(signature)), $"assinatura de metadado presente na saida: {signature}");
        }
    }

    private static void AssertLeftRightHalves(byte[] output)
    {
        using var bitmap = SKBitmap.Decode(output);
        Assert.True(IsRed(bitmap.GetPixel(16, 128)), "esquerda deveria ser vermelha");
        Assert.True(IsBlue(bitmap.GetPixel(240, 128)), "direita deveria ser azul");
    }

    private static bool IsRed(SKColor c) => c.Red > 200 && c.Green < 70 && c.Blue < 70;

    private static bool IsBlue(SKColor c) => c.Blue > 200 && c.Red < 70 && c.Green < 70;

    private static bool Contains(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    // ---- geracao de imagens (tudo em memoria) ----

    // esquerda vermelha, direita azul (orientacao do TOPO/BASE aparece apos a rotacao do EXIF)
    private static SKBitmap TwoHalves(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var red = new SKPaint { Color = new SKColor(255, 0, 0) };
        using var blue = new SKPaint { Color = new SKColor(0, 0, 255) };
        canvas.DrawRect(0, 0, width / 2f, height, red);
        canvas.DrawRect(width / 2f, 0, width - width / 2f, height, blue);
        return bitmap;
    }

    private static byte[] Encode(SKEncodedImageFormat format, int quality)
    {
        using var bitmap = TwoHalves(800, 600);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(format, quality).ToArray();
    }

    private static byte[] EncodeSolid(int width, int height, SKEncodedImageFormat format, int quality)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height),
                [new SKColor(200, 30, 30), new SKColor(30, 30, 200)],
                SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(0, 0, width, height, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(format, quality).ToArray();
    }

    private static byte[] NoisyPng(int width, int height)
    {
        var random = new Random(7);
        using var bitmap = new SKBitmap(width, height);
        var pixels = new byte[width * height * 4];
        random.NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static byte[] BuildJpegWithMetadata()
    {
        byte[] baseJpeg;
        using (var bitmap = TwoHalves(1600, 1200))
        using (var image = SKImage.FromBitmap(bitmap))
        {
            baseJpeg = image.Encode(SKEncodedImageFormat.Jpeg, 92).ToArray();
        }

        // ponto de insercao: logo apos SOI e, se existir, apos o APP0 (JFIF) do encoder.
        var insertAt = 2;
        if (baseJpeg[2] == 0xFF && baseJpeg[3] == 0xE0)
        {
            insertAt = 4 + ((baseJpeg[4] << 8) | baseJpeg[5]);
        }

        var segments = new List<byte[]>
        {
            Segment(0xE1, [.. "Exif\0\0"u8, .. ExifTiffWithOrientation6AndGps()]),
            Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><gps:GPSLatitude>23,33.0S</gps:GPSLatitude></x:xmpmeta>"u8]),
            Segment(0xE2, [.. "ICC_PROFILE\0"u8, 1, 1, .. new byte[200]]),
        };

        var current = baseJpeg.Length + segments.Sum(s => s.Length);
        var random = new Random(42);
        while (current < 1_500_000)
        {
            var filler = new byte[Math.Min(65_533, 1_500_000 - current)];
            if (filler.Length < 16)
            {
                break;
            }

            random.NextBytes(filler);
            var comment = Segment(0xFE, filler);
            segments.Add(comment);
            current += comment.Length;
        }

        var output = new List<byte>(current);
        output.AddRange(baseJpeg.AsSpan(0, insertAt).ToArray());
        foreach (var segment in segments)
        {
            output.AddRange(segment);
        }

        output.AddRange(baseJpeg.AsSpan(insertAt).ToArray());
        return [.. output];
    }

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var length = payload.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)(length & 0xFF), .. payload];
    }

    // TIFF big-endian: IFD0 {Orientation=6, GPSInfo->38}; IFD GPS {GPSLatitudeRef="S"}.
    private static byte[] ExifTiffWithOrientation6AndGps() =>
    [
        0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08, // "MM", 42, offset do IFD0 = 8
        0x00, 0x02, // IFD0: 2 entradas
        0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x06, 0x00, 0x00, // Orientation SHORT = 6
        0x88, 0x25, 0x00, 0x04, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x26, // GPSInfo LONG -> offset 38
        0x00, 0x00, 0x00, 0x00, // proximo IFD: nenhum
        0x00, 0x01, // IFD GPS: 1 entrada
        0x00, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02, (byte)'S', 0x00, 0x00, 0x00, // GPSLatitudeRef ASCII "S"
        0x00, 0x00, 0x00, 0x00,
    ];

    // ---- fixtures e codecs ----

    private static readonly string FixturesDir = LocateFixtures();

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(FixturesDir, name));

    private static string LocateFixtures()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Deckloom.Accounts.Infrastructure.Tests.csproj")))
            {
                return Path.Combine(dir.FullName, "Fixtures");
            }
        }

        throw new FileNotFoundException("csproj nao encontrado acima de " + AppContext.BaseDirectory);
    }

    private abstract class HeaderOnlyCodec : IAvatarCodec
    {
        public bool TryReadHeader(byte[] data) => true;

        public bool TryReadHeader(byte[] data, out AvatarHeaderInfo header)
        {
            header = new AvatarHeaderInfo(64, 64, 1, AvatarImageFormat.Png);
            return true;
        }

        public abstract bool TryReencode(byte[] data, int outputSide, int quality, out AvatarReencodeResult result);
    }

    // retorna o que o roteiro mandar; null = codec devolve false.
    private sealed class ScriptedCodec(Func<byte[], AvatarReencodeResult?> script) : HeaderOnlyCodec
    {
        public int Reencodes { get; private set; }

        public int LastOutputSide { get; private set; }

        public int LastQuality { get; private set; }

        public override bool TryReencode(byte[] data, int outputSide, int quality, out AvatarReencodeResult result)
        {
            Reencodes++;
            LastOutputSide = outputSide;
            LastQuality = quality;
            var scripted = script(data);
            result = scripted!;
            return scripted is not null;
        }
    }

    // bloqueia ate o portao abrir (ou 10 s) e mede a concorrencia; ignora qualquer cancelamento, como o codigo nativo.
    private sealed class BlockingCodec(ManualResetEventSlim gate) : HeaderOnlyCodec
    {
        private int _current;
        private int _max;
        private int _completed;

        public int Current => Volatile.Read(ref _current);

        public int MaxObserved => Volatile.Read(ref _max);

        public int Completed => Volatile.Read(ref _completed);

        public override bool TryReencode(byte[] data, int outputSide, int quality, out AvatarReencodeResult result)
        {
            var now = Interlocked.Increment(ref _current);
            int seen;
            while (now > (seen = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, now, seen) != seen)
            {
            }

            gate.Wait(TimeSpan.FromSeconds(10));
            Interlocked.Decrement(ref _current);
            Interlocked.Increment(ref _completed);
            result = new AvatarReencodeResult(new byte[] { 1 }, 256, 256);
            return true;
        }
    }

    private sealed class RecordingCodec(IAvatarCodec inner) : IAvatarCodec
    {
        public AvatarReencodeResult? Last { get; private set; }

        public bool TryReadHeader(byte[] data) => inner.TryReadHeader(data);

        public bool TryReadHeader(byte[] data, out AvatarHeaderInfo header) => inner.TryReadHeader(data, out header);

        public bool TryReencode(byte[] data, int outputSide, int quality, out AvatarReencodeResult result)
        {
            var ok = inner.TryReencode(data, outputSide, quality, out result);
            Last = ok ? result : null;
            return ok;
        }
    }
}
