using System.Text.RegularExpressions;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Infrastructure;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-070 (REQ-019, REQ-020; ADR-0011 passo 1): tamanho e assinatura sao verificados ANTES de qualquer decodificacao.
// Costura exigida do GREEN: `IAvatarCodec` (namespace Deckloom.Accounts.Infrastructure) com
// `bool TryReadHeader(byte[] data)` - unico ponto de contato com o SKCodec; `SkiaAvatarImageProcessor` tem
// construtor `()` (codec real, SkiaSharp) e `(IAvatarCodec codec)` (spy). Rejeicoes por tamanho/tipo nao chamam o codec.
[Collection("AvatarProcessorSemaphore")]
public class AvatarSignatureTests
{
    private const int MaxBytes = 2_097_152;

    private static readonly string FixturesDir = LocateFixtures();

    // ---- aceitos pela etapa de assinatura (nao sao rejeitados por Size nem Type) ----

    [Theory]
    [InlineData("exif-gps-orientation6.jpg")]
    [InlineData("pixel.png")]
    [InlineData("pixel.webp")]
    public async Task Jpeg_png_and_webp_signatures_pass_size_and_type_stages_REQ_020(string fixture)
    {
        var codec = new SpyCodec();
        var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(Fixture(fixture));

        AssertNotRejectedBy(result, AvatarRejectionReason.Size, AvatarRejectionReason.Type);
        Assert.Equal(1, codec.HeaderReads); // chegou a etapa de cabecalho
    }

    [Theory]
    [InlineData("pixel.png", "foto.exe", "application/x-msdownload")]
    [InlineData("exif-gps-orientation6.jpg", "foto.gif", "image/gif")]
    [InlineData("pixel.webp", "foto.txt", "text/plain")]
    public async Task Declared_extension_and_content_type_are_ignored_REQ_020(string fixture, string declaredName, string declaredContentType)
    {
        // A interface so recebe os bytes: extensao e Content-Type nao influenciam; o tipo vem da assinatura.
        Assert.NotEmpty(declaredName);
        Assert.NotEmpty(declaredContentType);

        var result = await new SkiaAvatarImageProcessor(new SpyCodec()).ProcessAsync(Fixture(fixture));

        AssertNotRejectedBy(result, AvatarRejectionReason.Size, AvatarRejectionReason.Type);
    }

    [Fact]
    public async Task Body_of_exactly_2097152_bytes_passes_the_size_stage_REQ_019()
    {
        var body = WithSignature(Fixture("pixel.png"), MaxBytes);
        Assert.Equal(MaxBytes, body.Length);

        var result = await new SkiaAvatarImageProcessor(new SpyCodec()).ProcessAsync(body);

        AssertNotRejectedBy(result, AvatarRejectionReason.Size);
    }

    // ---- rejeicoes por tamanho ----

    [Fact]
    public async Task Png_of_2_1_MB_is_rejected_by_size_without_decoding_REQ_019()
    {
        var codec = new SpyCodec();
        var body = WithSignature(Fixture("pixel.png"), 2_202_010); // ~2,1 MB

        var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(body);

        AssertRejected(result, AvatarRejectionReason.Size);
        Assert.Equal(0, codec.Calls);
    }

    [Fact]
    public async Task Body_of_2097153_bytes_is_rejected_by_size_REQ_019()
    {
        var codec = new SpyCodec();

        var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(WithSignature(Fixture("pixel.png"), MaxBytes + 1));

        AssertRejected(result, AvatarRejectionReason.Size);
        Assert.Equal(0, codec.Calls);
    }

    // ---- rejeicoes por tipo ----

    [Fact]
    public async Task Gif_is_rejected_by_type_without_decoding_REQ_020()
        => await AssertTypeRejectedWithoutDecoding(Fixture("pixel.gif"));

    [Fact]
    public async Task Executable_with_MZ_signature_is_rejected_by_type_without_decoding_REQ_020()
        => await AssertTypeRejectedWithoutDecoding([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00]);

    [Fact]
    public async Task Text_file_named_foto_png_is_rejected_by_type_without_decoding_REQ_020()
        => await AssertTypeRejectedWithoutDecoding("isto nao e uma imagem, apenas texto (foto.png)"u8.ToArray());

    [Fact]
    public async Task Empty_and_truncated_bodies_are_rejected_by_type_without_decoding_REQ_020()
    {
        await AssertTypeRejectedWithoutDecoding([]);
        await AssertTypeRejectedWithoutDecoding([0xFF, 0xD8]);
        await AssertTypeRejectedWithoutDecoding([0x89, 0x50, 0x4E, 0x47]);
        await AssertTypeRejectedWithoutDecoding("RIFF\0\0\0\0WEB"u8.ToArray());
    }

    [Fact]
    public async Task Riff_container_that_is_not_webp_is_rejected_by_type_REQ_020()
        => await AssertTypeRejectedWithoutDecoding("RIFF\u0024\0\0\0WAVEfmt "u8.ToArray());

    // ---- assinatura valida, corpo corrompido (codec real, SkiaSharp no GREEN) ----

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x01, 0x02, 0x03 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02 })]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x04, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 })]
    public async Task Valid_signature_with_corrupted_body_is_rejected_by_content_REQ_020(byte[] body)
    {
        var result = await new SkiaAvatarImageProcessor().ProcessAsync(body);

        AssertRejected(result, AvatarRejectionReason.Content);
    }

    [Fact]
    public async Task Valid_signature_with_unreadable_header_is_rejected_by_content_REQ_020()
    {
        var codec = new SpyCodec(); // devolve false: cabecalho ilegivel

        var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(Fixture("pixel.png"));

        AssertRejected(result, AvatarRejectionReason.Content);
    }

    // ---- higiene da dependencia (ADR-0011: versao exata fixada) ----

    [Theory]
    [InlineData("SkiaSharp")]
    [InlineData("SkiaSharp.NativeAssets.Linux.NoDependencies")]
    public void Skia_packages_are_pinned_to_an_exact_version_in_Directory_Packages_props(string package)
    {
        var props = File.ReadAllText(LocateUp("Directory.Packages.props"));

        var match = Regex.Match(props, "<PackageVersion\\s+Include=\"" + Regex.Escape(package) + "\"\\s+Version=\"(?<v>[^\"]*)\"\\s*/>");

        Assert.True(match.Success, $"{package} ausente em Directory.Packages.props");
        Assert.Matches(@"^\d+\.\d+\.\d+(\.\d+)?$", match.Groups["v"].Value); // sem intervalos, curingas ou prereleases
    }

    // ---- helpers ----

    private static async Task AssertTypeRejectedWithoutDecoding(byte[] body)
    {
        var codec = new SpyCodec();

        var result = await new SkiaAvatarImageProcessor(codec).ProcessAsync(body);

        AssertRejected(result, AvatarRejectionReason.Type);
        Assert.Equal(0, codec.Calls);
    }

    private static void AssertRejected(AvatarProcessingResult result, AvatarRejectionReason reason)
    {
        var rejected = Assert.IsType<AvatarRejected>(result);
        Assert.Equal(reason, rejected.Reason);
    }

    private static void AssertNotRejectedBy(AvatarProcessingResult result, params AvatarRejectionReason[] reasons)
    {
        if (result is AvatarRejected rejected)
        {
            Assert.DoesNotContain(rejected.Reason, reasons);
        }
    }

    /// <summary>Preenche com zeros ate o tamanho pedido mantendo a assinatura do corpo original.</summary>
    private static byte[] WithSignature(byte[] original, int length)
    {
        var body = new byte[length];
        original.CopyTo(body, 0);
        return body;
    }

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(FixturesDir, name));

    private static string LocateFixtures()
    {
        var dir = Path.GetDirectoryName(LocateUp("Deckloom.Accounts.Infrastructure.Tests.csproj"))!;
        return Path.Combine(dir, "Fixtures");
    }

    private static string LocateUp(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{fileName} nao encontrado acima de {AppContext.BaseDirectory}");
    }

    private sealed class SpyCodec : IAvatarCodec
    {
        public int HeaderReads { get; private set; }

        public int Calls => HeaderReads;

        public bool TryReadHeader(byte[] data)
        {
            HeaderReads++;
            return false;
        }
    }
}
