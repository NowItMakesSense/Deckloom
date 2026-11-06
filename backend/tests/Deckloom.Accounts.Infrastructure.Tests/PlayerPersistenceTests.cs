using System.Globalization;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-055 (REQ-001, 002, 033, 044, 048). Contrato de AccountsDbContext/EfPlayerRepository: ver SqliteInMemoryFixture.
public sealed class PlayerPersistenceTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteInMemoryFixture _db = new();

    public void Dispose() => _db.Dispose();

    private static Player PasswordPlayer(string email = "ana@example.com") =>
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

    private static Player GooglePlayer() =>
        Player.RegisterWithGoogle(
            Email.Create("gabi@example.com").Value,
            ExternalIdentity.Create("google", "sub-123").Value,
            DisplayName.Create("Gabi Lima").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-1").Value;

    private async Task<Player> Persist(Player player)
    {
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(player);
        await new EfUnitOfWork(context).CommitAsync();
        return player;
    }

    private async Task<Player> Reload(PlayerId id)
    {
        await using var context = _db.CreateContext();
        return (await new EfPlayerRepository(context).GetById(id))!;
    }

    private T Scalar<T>(string sql)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task REQ_001_password_player_round_trips_through_the_context()
    {
        var saved = await Persist(PasswordPlayer());

        var loaded = await Reload(saved.Id);

        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal(saved.Email, loaded.Email);
        Assert.Equal(Stored, loaded.PasswordHash!.ToStoredString());
        Assert.Equal(saved.DisplayName, loaded.DisplayName);
        Assert.Equal(saved.Avatar, loaded.Avatar);
    }

    [Fact]
    public async Task REQ_002_google_player_has_null_password_hash()
    {
        var saved = await Persist(GooglePlayer());

        var loaded = await Reload(saved.Id);

        Assert.Null(loaded.PasswordHash);
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM player WHERE password_hash IS NULL"));
    }

    [Fact]
    public async Task REQ_033_terms_acceptance_and_adult_declaration_are_persisted()
    {
        var saved = await Persist(PasswordPlayer());

        var loaded = await Reload(saved.Id);

        Assert.Equal(Terms, loaded.TermsAcceptance.TermsVersion);
        Assert.Equal(Now, loaded.TermsAcceptance.AcceptedAt);
        Assert.True(loaded.TermsAcceptance.AdultDeclared);
        Assert.Equal(Terms, Scalar<string>("SELECT terms_version FROM player"));
        Assert.Equal(1, Scalar<int>("SELECT adult_declared FROM player"));
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM player WHERE terms_accepted_at IS NOT NULL"));
    }

    [Fact]
    public async Task REQ_044_favorite_tcg_is_stored_as_mtg()
    {
        var saved = await Persist(PasswordPlayer());

        var loaded = await Reload(saved.Id);

        Assert.Equal(FavoriteTcg.Default, loaded.FavoriteTcg);
        Assert.Equal("mtg", Scalar<string>("SELECT favorite_tcg FROM player"));
    }

    [Fact]
    public async Task REQ_001_email_is_unique_through_ux_player_email()
    {
        await Persist(PasswordPlayer());

        var count = Scalar<int>(
            "SELECT COUNT(*) FROM pragma_index_list('player') WHERE name = 'ux_player_email' AND \"unique\" = 1");

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task REQ_001_second_add_with_same_email_is_translated_to_unique_violation_for_email_unavailable()
    {
        await Persist(PasswordPlayer());
        await using var context = _db.CreateContext();
        var repository = new EfPlayerRepository(context);

        await repository.Add(PasswordPlayer());

        var thrown = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => new EfUnitOfWork(context).CommitAsync());

        Assert.Equal("ux_player_email", thrown.ConstraintName);
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM player"));
    }

    [Fact]
    public async Task REQ_048_password_column_stores_only_the_hash_never_plain_text()
    {
        await Persist(PasswordPlayer());

        var column = Scalar<string>("SELECT password_hash FROM player");

        Assert.Equal(Stored, column);
        Assert.StartsWith("v1$pbkdf2-sha256$", column, StringComparison.Ordinal);
    }

    [Fact]
    public async Task REQ_048_stale_write_is_rejected_by_the_optimistic_concurrency_token()
    {
        var saved = await Persist(PasswordPlayer());
        await using var first = _db.CreateContext();
        await using var second = _db.CreateContext();
        var firstRepository = new EfPlayerRepository(first);
        var secondRepository = new EfPlayerRepository(second);
        var a = (await firstRepository.GetById(saved.Id))!;
        var b = (await secondRepository.GetById(saved.Id))!;

        a.UpdateProfile("Ana Souza", "bio A", "mtg", [], []);
        await firstRepository.Save(a);
        await new EfUnitOfWork(first).CommitAsync();
        b.UpdateProfile("Ana Souza", "bio B", "mtg", [], []);
        await secondRepository.Save(b);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => new EfUnitOfWork(second).CommitAsync());
        Assert.Equal("bio A", (await Reload(saved.Id)).Bio!.Value);
    }
}
