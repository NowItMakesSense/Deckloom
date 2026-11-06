using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure;

// SaveAsync e RemoveAsync so ENFILEIRAM no contexto; a gravacao acontece no EfUnitOfWork.CommitAsync, na mesma
// transacao do Jogador (ADR-0009). O dono da imagem e o Jogador rastreado no mesmo contexto cujo CustomAvatar tem a
// chave (a porta nao recebe o PlayerId). ReadAsync devolve uma copia dos bytes ou nulo.
public sealed class PostgresAvatarStorage(AccountsDbContext context, TimeProvider? time = null, int maxBytes = 262144) : IAvatarStorage
{
    // Limite de tamanho da imagem em bytes (padrao 256 KB, P2 do design).
    private readonly int _maxBytes = maxBytes;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private DbSet<AvatarImageRow> Rows => context.Set<AvatarImageRow>();

    public async Task SaveAsync(string key, byte[] image, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(image);
        if (image.Length > _maxBytes)
        {
            throw new ArgumentException($"Image exceeds the limit of {_maxBytes} bytes.", nameof(image));
        }

        // Save em chave existente SUBSTITUI a imagem (contrato de IAvatarStorage).
        // So substitui a linha do PROPRIO dono; chave de outro Jogador segue para o Add e colide na PK no commit.
        var owner = OwnerOf(key);
        var ownsExisting = await Rows.AsNoTracking().AnyAsync(i => i.StorageKey == key && i.PlayerId == owner, ct);
        if (ownsExisting)
        {
            var existing = (await Rows.FindAsync([key], ct))!;
            existing.Content = image.ToArray();
            existing.CreatedAt = _time.GetUtcNow();
            return;
        }

        Rows.Add(new AvatarImageRow
        {
            StorageKey = key,
            PlayerId = owner,
            Content = image.ToArray(),
            CreatedAt = _time.GetUtcNow(),
        });
    }

    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct = default) =>
        await Rows.AsNoTracking().Where(i => i.StorageKey == key).Select(i => i.Content).FirstOrDefaultAsync(ct);

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var row = await Rows.FindAsync([key], ct);
        if (row is not null)
        {
            Rows.Remove(row);
        }
    }

    private PlayerId OwnerOf(string key)
    {
        var owner = context.ChangeTracker.Entries<Player>()
            .Select(e => e.Entity)
            .FirstOrDefault(p => p.Avatar is CustomAvatar custom && custom.StorageKey == key);
        return owner?.Id
            ?? throw new InvalidOperationException("No tracked Player has a custom avatar with the given storage key.");
    }
}
