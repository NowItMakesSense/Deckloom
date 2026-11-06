using Deckloom.Accounts.Application;
using Deckloom.Accounts.Infrastructure;
using SkiaSharp;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-094: a composicao liga Avatar:MaxDimension e Avatar:MaxPixels ao processador de producao sem expor o codec interno.
[Collection("AvatarProcessorSemaphore")]
public class AvatarProcessorLimitsWiringTests
{
    [Fact]
    public async Task Public_constructor_applies_the_configured_dimension_limit()
    {
        var png = EncodePng(100, 100);

        var result = await new SkiaAvatarImageProcessor(maxDimension: 50, maxPixels: 1_000_000).ProcessAsync(png);

        var rejected = Assert.IsType<AvatarRejected>(result);
        Assert.Equal(AvatarRejectionReason.Content, rejected.Reason);
    }

    [Fact]
    public async Task Public_constructor_accepts_images_within_the_configured_limits()
    {
        var png = EncodePng(100, 100);

        var result = await new SkiaAvatarImageProcessor(maxDimension: 200, maxPixels: 1_000_000).ProcessAsync(png);

        Assert.IsType<AvatarAccepted>(result);
    }

    private static byte[] EncodePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
