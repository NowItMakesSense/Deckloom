namespace Deckloom.Accounts.Infrastructure;

/// <summary>Unico ponto de contato com o decodificador nativo (SKCodec); permite testar a ordem das etapas.</summary>
public interface IAvatarCodec
{
    /// <summary>Le somente o cabecalho da imagem; false quando ilegivel.</summary>
    bool TryReadHeader(byte[] data);

    /// <summary>Le dimensoes, quadros e formato somente do cabecalho (sem pixels); false quando ilegivel.</summary>
    bool TryReadHeader(byte[] data, out AvatarHeaderInfo header)
        => throw new NotImplementedException();

    /// <summary>
    /// Decodifica (amostragem reduzida acima de 512 px, orientacao aplicada aos pixels), recorta ao centro,
    /// redimensiona para outputSide x outputSide e codifica WebP sem metadados; false quando falha.
    /// </summary>
    bool TryReencode(byte[] data, int outputSide, int quality, out AvatarReencodeResult result)
        => throw new NotImplementedException();
}

/// <summary>Imagem reencodada e o tamanho real da amostra decodificada (para provar a amostragem reduzida).</summary>
public sealed record AvatarReencodeResult(byte[] Image, int DecodedWidth, int DecodedHeight);
