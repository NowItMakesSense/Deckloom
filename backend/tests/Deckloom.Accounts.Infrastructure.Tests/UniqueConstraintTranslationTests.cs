using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-063 (REQ-024, 027). Pendencia T-059/T-061: a violacao de unicidade de avatar_image ainda nao e traduzida.
// Contrato para o implementer (GREEN): EfUnitOfWork.CommitAsync traduz tambem
//   ux_avatar_image_player_id  -> UniqueConstraintViolationException("ux_avatar_image_player_id")
//   PK de avatar_image         -> UniqueConstraintViolationException("pk_avatar_image")
// (SQLite nomeia a coluna "avatar_image.player_id"/"avatar_image.storage_key"; PostgreSQL nomeia o indice/constraint.)
// Nenhuma excecao do EF/Sqlite vaza pela porta. ux_player_email ja e traduzida (guarda de regressao).
public sealed class UniqueConstraintTranslationTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Image = [1, 2, 3];

    private readonly SqliteInMemoryFixture _db = new();

    public void Dispose() => _db.Dispose();

    private static Player NewPlayer(string email) =>
        Player.RegisterWithPassword(
            Email.Create(email).Value,
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create("Ana Souza").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-1").Value;

    private async Task<PlayerId> Persist(string email)
    {
        var player = NewPlayer(email);
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(player);
        await new EfUnitOfWork(context).CommitAsync();
        return player.Id;
    }

    private async Task GiveCustomAvatar(PlayerId id, string key)
    {
        await using var context = _db.CreateContext();
        var players = new EfPlayerRepository(context);
        var player = (await players.GetById(id))!;
        Assert.True(player.SetCustomAvatar(key).IsSuccess);
        await new PostgresAvatarStorage(context).SaveAsync(key, Image);
        await players.Save(player);
        await new EfUnitOfWork(context).CommitAsync();
    }

    [Fact]
    public async Task REQ_027_a_duplicate_email_at_commit_is_translated_to_ux_player_email()
    {
        await Persist("ana@example.com");

        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(NewPlayer("ana@example.com"));

        var thrown = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => new EfUnitOfWork(context).CommitAsync());
        Assert.Equal("ux_player_email", thrown.ConstraintName);
    }

    [Fact]
    public async Task REQ_024_a_second_image_for_the_same_player_at_commit_is_translated_to_ux_avatar_image_player_id()
    {
        var ana = await Persist("ana@example.com");
        await GiveCustomAvatar(ana, "key-a");

        // Troca de avatar sem remover a imagem anterior: duas linhas para o mesmo Jogador.
        await using var context = _db.CreateContext();
        var players = new EfPlayerRepository(context);
        var player = (await players.GetById(ana))!;
        Assert.True(player.SetCustomAvatar("key-b").IsSuccess);
        await new PostgresAvatarStorage(context).SaveAsync("key-b", Image);
        await players.Save(player);

        var thrown = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => new EfUnitOfWork(context).CommitAsync());
        Assert.Equal("ux_avatar_image_player_id", thrown.ConstraintName);
    }

    [Fact]
    public async Task REQ_024_a_storage_key_already_owned_by_another_player_at_commit_is_translated_to_pk_avatar_image()
    {
        var ana = await Persist("ana@example.com");
        var bia = await Persist("bia@example.com");
        await GiveCustomAvatar(bia, "key-b");

        await using var context = _db.CreateContext();
        var players = new EfPlayerRepository(context);
        var player = (await players.GetById(ana))!;
        Assert.True(player.SetCustomAvatar("key-b").IsSuccess);
        await new PostgresAvatarStorage(context).SaveAsync("key-b", Image);
        await players.Save(player);

        var thrown = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => new EfUnitOfWork(context).CommitAsync());
        Assert.Equal("pk_avatar_image", thrown.ConstraintName);
    }
}
