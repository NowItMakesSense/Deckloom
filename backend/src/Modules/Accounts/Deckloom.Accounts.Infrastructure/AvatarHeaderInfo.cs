namespace Deckloom.Accounts.Infrastructure;

/// <summary>Metadados lidos somente do cabecalho (sem decodificar pixels).</summary>
public sealed record AvatarHeaderInfo(int Width, int Height, int FrameCount, AvatarImageFormat Format);
