using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure;

// Linha de Database/tables/accounts.avatar_image.md: imagem ja reencodada (WebP) do avatar customizado de um Jogador.
internal sealed class AvatarImageRow
{
    public const string WebpContentType = "image/webp";

    public required string StorageKey { get; set; }

    public PlayerId PlayerId { get; set; }

    public string ContentType { get; set; } = WebpContentType;

    public required byte[] Content { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
