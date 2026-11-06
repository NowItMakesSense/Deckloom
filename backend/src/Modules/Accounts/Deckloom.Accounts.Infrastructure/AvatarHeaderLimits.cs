namespace Deckloom.Accounts.Infrastructure;

/// <summary>Regras da etapa 3 (REQ-049), puras: operam so sobre metadados do cabecalho.</summary>
public static class AvatarHeaderLimits
{
    public static bool ExceedsPixels(int width, int height, long maxPixels)
        => (long)width * height > maxPixels;

    public static bool IsAccepted(AvatarHeaderInfo header, AvatarImageFormat signatureFormat, int maxDimension, long maxPixels)
        => header.Format == signatureFormat
            && header.Width <= maxDimension
            && header.Height <= maxDimension
            && header.FrameCount <= 1
            && !ExceedsPixels(header.Width, header.Height, maxPixels);
}
