using System.Globalization;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-133 (decisao do usuario). Save de Jogador ou Sessao DESANEXADO (nao rastreado pelo contexto) lanca
// InvalidOperationException em vez de ser no-op silencioso; entidade rastreada (carregada pelo repositorio) segue persistindo.
public sealed class DetachedSavePersistenceTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2020, 3, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Sha256TokenHasher Hasher = new();

    private readonly SqliteInMemoryFixture _db = new();

    public void Dispose() => _db.Dispose();

    private static Player NewPlayer(string email = "ana@example.com") =>
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

    private async Task Persist(Player player)
    {
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(player);
        await new EfUnitOfWork(context).CommitAsync();
    }

    private T Scalar<T>(string sql)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? default! : (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task REQ_010_Save_de_Jogador_desanexado_lanca_InvalidOperationException_e_nao_grava()
    {
        var player = NewPlayer();
        var before = Scalar<long>("SELECT COUNT(*) FROM player");

        await using (var context = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(context);

            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.Save(player));
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(before, Scalar<long>("SELECT COUNT(*) FROM player"));
    }

    [Fact]
    public async Task REQ_010_Save_de_Sessao_desanexada_lanca_InvalidOperationException_e_nao_grava()
    {
        var player = NewPlayer();
        await Persist(player);
        var session = Session.Start(player.Id, Hasher.Hash("tok-detached"), false, Now);
        var before = Scalar<long>("SELECT COUNT(*) FROM player_session");

        await using (var context = _db.CreateContext())
        {
            var repository = new EfSessionRepository(context);

            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.Save(session));
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(before, Scalar<long>("SELECT COUNT(*) FROM player_session"));
    }

    [Fact]
    public async Task REQ_010_Save_de_Jogador_rastreado_continua_persistindo_a_mudanca()
    {
        await Persist(NewPlayer());

        await using (var context = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(context);
            var tracked = await repository.FindByEmail(Email.Create("ana@example.com").Value);
            Assert.NotNull(tracked);
            Assert.True(tracked.UpdateProfile("Ana Souza", "Nova bio", "mtg", [], []).IsSuccess);

            await repository.Save(tracked);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal("Nova bio", Scalar<string>("SELECT bio FROM player"));
    }

    [Fact]
    public async Task REQ_010_Save_de_Sessao_rastreada_continua_persistindo_a_mudanca()
    {
        var player = NewPlayer();
        await Persist(player);
        await using (var seed = _db.CreateContext())
        {
            await new EfSessionRepository(seed).Add(Session.Start(player.Id, Hasher.Hash("tok-tracked"), false, Now));
            await new EfUnitOfWork(seed).CommitAsync();
        }

        await using (var context = _db.CreateContext())
        {
            var repository = new EfSessionRepository(context);
            var tracked = await repository.FindByTokenHash(Hasher.Hash("tok-tracked"));
            Assert.NotNull(tracked);
            tracked.Revoke(SessionRevocationReason.Logout, Now.AddMinutes(1));

            await repository.Save(tracked);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.Equal(1L, Scalar<long>("SELECT COUNT(*) FROM player_session WHERE revoked_at IS NOT NULL"));
    }
}
