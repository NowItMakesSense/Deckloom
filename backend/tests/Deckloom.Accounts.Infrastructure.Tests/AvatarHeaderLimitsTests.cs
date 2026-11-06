using Deckloom.Accounts.Application;
using Deckloom.Accounts.Infrastructure;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-071 (REQ-049, REQ-020; ADR-0011 passo 3): dimensoes e animacao saem SOMENTE do cabecalho.
// Costura exigida do GREEN: `IAvatarCodec.TryReadHeader(byte[], out AvatarHeaderInfo)` (sem pixels),
// `AvatarHeaderLimits` (regra pura) e o processador chamando essa leitura apos a assinatura.
// O spy so expoe leitura de cabecalho (nao ha metodo de decodificacao de pixels no contrato do codec nesta etapa):
// recusar via essa unica capacidade prova que nenhum pixel foi decodificado.
[Collection("AvatarProcessorSemaphore")]
public class AvatarHeaderLimitsTests
{
    private const int MaxDimension = 4096;
    private const long MaxPixels = 16_777_216;

    private static readonly string FixturesDir = LocateFixtures();

    // ---- regra pura ----

    [Fact]
    public void Header_4096x4096_is_accepted_REQ_049()
        => Assert.True(AvatarHeaderLimits.IsAccepted(Png(4096, 4096), AvatarImageFormat.Png, MaxDimension, MaxPixels));

    [Theory]
    [InlineData(4097, 4096)]
    [InlineData(4096, 4097)]
    [InlineData(4097, 100)]
    [InlineData(100, 4097)]
    [InlineData(20000, 20000)]
    public void Header_with_a_side_above_4096_is_refused_REQ_049(int width, int height)
        => Assert.False(AvatarHeaderLimits.IsAccepted(Png(width, height), AvatarImageFormat.Png, MaxDimension, MaxPixels));

    [Theory]
    [InlineData(2)]
    [InlineData(60)]
    public void Animated_header_with_more_than_one_frame_is_refused_REQ_049(int frames)
        => Assert.False(AvatarHeaderLimits.IsAccepted(new AvatarHeaderInfo(64, 64, frames, AvatarImageFormat.Webp), AvatarImageFormat.Webp, MaxDimension, MaxPixels));

    [Fact]
    public void Single_frame_header_is_accepted_REQ_049()
        => Assert.True(AvatarHeaderLimits.IsAccepted(new AvatarHeaderInfo(64, 64, 1, AvatarImageFormat.Webp), AvatarImageFormat.Webp, MaxDimension, MaxPixels));

    [Theory]
    [InlineData(AvatarImageFormat.Png, AvatarImageFormat.Webp)]
    [InlineData(AvatarImageFormat.Jpeg, AvatarImageFormat.Png)]
    [InlineData(AvatarImageFormat.Webp, AvatarImageFormat.Jpeg)]
    public void Codec_format_different_from_signature_is_refused_REQ_049(AvatarImageFormat signature, AvatarImageFormat codecFormat)
        => Assert.False(AvatarHeaderLimits.IsAccepted(new AvatarHeaderInfo(64, 64, 1, codecFormat), signature, MaxDimension, MaxPixels));

    [Theory]
    [InlineData(65535, 65535)] // produto 4_294_836_225 estoura int (vira negativo)
    [InlineData(46341, 46341)] // 2_147_488_281 estoura int por pouco
    [InlineData(4096, 4097)] // 16_781_312 > 16_777_216
    public void Pixel_product_is_computed_in_long_and_exceeds_the_limit_REQ_049(int width, int height)
        => Assert.True(AvatarHeaderLimits.ExceedsPixels(width, height, MaxPixels));

    [Theory]
    [InlineData(4096, 4096)] // exatamente 16_777_216, nao excede
    [InlineData(1, 1)]
    public void Pixel_product_at_or_below_the_limit_does_not_exceed_REQ_049(int width, int height)
        => Assert.False(AvatarHeaderLimits.ExceedsPixels(width, height, MaxPixels));

    [Fact]
    public void Limits_are_parameters_not_hardcoded_REQ_049()
    {
        Assert.True(AvatarHeaderLimits.IsAccepted(Png(512, 512), AvatarImageFormat.Png, 512, MaxPixels));
        Assert.False(AvatarHeaderLimits.IsAccepted(Png(513, 512), AvatarImageFormat.Png, 512, MaxPixels));
        Assert.False(AvatarHeaderLimits.IsAccepted(Png(512, 512), AvatarImageFormat.Png, 512, 100_000));
    }

    // ---- processador (spy do codec: so existe leitura de cabecalho, nunca pixels) ----

    [Fact]
    public async Task Processor_reads_header_info_once_for_4096x4096_REQ_049()
    {
        var codec = new HeaderSpyCodec(Png(4096, 4096));

        await new SkiaAvatarImageProcessor(codec).ProcessAsync(Fixture("pixel.png"));

        Assert.Equal(1, codec.InfoReads);
    }

    [Theory]
    [InlineData(4097, 4096)]
    [InlineData(4096, 4097)]
    [InlineData(4097, 100)]
    [InlineData(100, 4097)]
    [InlineData(20000, 20000)]
    public async Task Processor_refuses_oversized_header_as_content_after_reading_only_the_header_REQ_049(int width, int height)
    {
        var codec = new HeaderSpyCodec(Png(width, height));

        var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(Fixture("pixel.png"));

        AssertContentRejected(result);
        Assert.Equal(1, codec.InfoReads);
    }

    [Theory]
    [InlineData("animated.webp", AvatarImageFormat.Webp)]
    [InlineData("animated.apng", AvatarImageFormat.Png)]
    public async Task Processor_refuses_animated_images_as_content_REQ_049(string fixture, AvatarImageFormat format)
    {
        var codec = new HeaderSpyCodec(new AvatarHeaderInfo(64, 64, 3, format));

        var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(Fixture(fixture));

        AssertContentRejected(result);
        Assert.Equal(1, codec.InfoReads);
    }

    [Fact]
    public async Task Processor_refuses_codec_format_different_from_signature_as_content_REQ_049()
    {
        var codec = new HeaderSpyCodec(new AvatarHeaderInfo(64, 64, 1, AvatarImageFormat.Webp));

        var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(Fixture("pixel.png"));

        AssertContentRejected(result);
        Assert.Equal(1, codec.InfoReads);
    }

    [Fact]
    public async Task All_header_rejections_use_the_same_generic_code_REQ_020()
    {
        var reasons = new List<AvatarRejectionReason>();
        var headers = new[]
        {
            Png(4097, 4096),
            Png(20000, 20000),
            new AvatarHeaderInfo(64, 64, 3, AvatarImageFormat.Png),
            new AvatarHeaderInfo(64, 64, 1, AvatarImageFormat.Webp),
        };

        foreach (var header in headers)
        {
            var codec = new HeaderSpyCodec(header);
            var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(Fixture("pixel.png"));
            reasons.Add(Assert.IsType<AvatarRejected>(result).Reason);
            Assert.Equal(1, codec.InfoReads);
        }

        Assert.All(reasons, reason => Assert.Equal(AvatarRejectionReason.Content, reason));
    }

    // ---- codec real (SkiaSharp): cabecalho declarado enorme e animados recusados ----

    [Theory]
    [InlineData("declared-20000x20000.png")]
    [InlineData("animated.webp")]
    [InlineData("animated.apng")]
    public async Task Real_codec_refuses_oversized_and_animated_fixtures_as_content_REQ_049(string fixture)
    {
        var result = await new SkiaAvatarImageProcessor().ProcessAsync(Fixture(fixture));

        AssertContentRejected(result);
    }

    // ---- helpers ----

    private static AvatarHeaderInfo Png(int width, int height) => new(width, height, 1, AvatarImageFormat.Png);

    private static void AssertContentRejected(AvatarProcessingResult result)
        => Assert.Equal(AvatarRejectionReason.Content, Assert.IsType<AvatarRejected>(result).Reason);

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(FixturesDir, name));

    private static string LocateFixtures()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Deckloom.Accounts.Infrastructure.Tests.csproj");
            if (File.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "Fixtures");
            }
        }

        throw new FileNotFoundException("csproj nao encontrado acima de " + AppContext.BaseDirectory);
    }

    private sealed class HeaderSpyCodec(AvatarHeaderInfo header) : IAvatarCodec
    {
        public int InfoReads { get; private set; }

        public bool TryReadHeader(byte[] data) => true;

        public bool TryReadHeader(byte[] data, out AvatarHeaderInfo info)
        {
            InfoReads++;
            info = header;
            return true;
        }
    }
}
