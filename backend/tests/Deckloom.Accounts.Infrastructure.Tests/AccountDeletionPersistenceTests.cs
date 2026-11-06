using System.Globalization;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-063 (REQ-024, 025, 027, 047, 048). Contrato para o implementer (GREEN):
//   Excluir o Player dentro de EfUnitOfWork.ExecuteAsync remove NA MESMA transacao, por cascata do banco (sem
//   exclusao manual onde a cascata cobre): player_session, player_external_identity, player_preferred_format,
//   password_reset_token, pending_google_auth com target_player_id = Jogador e avatar_image; aceite de termos e 18+
//   sao colunas da linha e somem com ela. access_attempt (sem FK) e apagada por
//   EfAccessAttemptStore.DeleteByAccountAsync(account_key_hash do email), enfileirada e efetivada no mesmo commit.
public sealed class AccountDeletionPersistenceTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Sha256TokenHasher Hasher = new();
    private static readonly byte[] IpKey = Enumerable.Repeat((byte)0xA1, 32).ToArray();
    private static readonly byte[] AnaAccountKey = Enumerable.Repeat((byte)0x11, 32).ToArray();
    private static readonly byte[] BiaAccountKey = Enumerable.Repeat((byte)0x22, 32).ToArray();

    private static readonly string[] OwnedTables =
    [
        "player_session",
        "player_external_identity",
        "player_preferred_format",
        "password_reset_token",
        "avatar_image",
    ];

    private readonly SqliteInMemoryFixture _db = new();

    public void Dispose() => _db.Dispose();

    private static Player NewPlayer(string email, string name) =>
        Player.RegisterWithPassword(
            Email.Create(email).Value,
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create(name).Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-1").Value;

    // O construtor de PasswordProof e internal (so Application o enxerga): o teste o cria por reflexao.
    private static PasswordProof ProofFor(PlayerId id) =>
        (PasswordProof)Activator.CreateInstance(
            typeof(PasswordProof),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            [id],
            null)!;

    // Jogador completo: preferencias, vinculo Google, token de redefinicao, avatar customizado, sessao,
    // pendencia alvo (link), pendencia sem alvo (signup) e dois eventos de acesso.
    private async Task<PlayerId> Populate(string email, string name, string tag, byte[] accountKey)
    {
        _db.SeedFormats();
        var player = NewPlayer(email, name);
        FormatId[] formats = [new("commander"), new("modern")];
        Assert.True(player.UpdateProfile(name, null, "mtg", formats, formats).IsSuccess);
        Assert.True(player.LinkGoogle(
            ExternalIdentity.Create("google", "sub-" + tag).Value, player.Email, ProofFor(player.Id)).IsSuccess);
        Assert.True(player.IssuePasswordReset(Hasher.Hash("reset-" + tag), Now).IsSuccess);
        Assert.True(player.SetCustomAvatar("key-" + tag).IsSuccess);

        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(player);
        await new PostgresAvatarStorage(context).SaveAsync("key-" + tag, [1, 2, 3]);
        await new EfSessionRepository(context).Add(Session.Start(player.Id, Hasher.Hash("session-" + tag), false, Now));
        var pending = new EfPendingGoogleAuthStore(context, TimeProvider.System);
        await pending.AddAsync(PendingGoogleLink.Create(
            Hasher.Hash("link-" + tag), "sub-link-" + tag, player.Email, player.Id, DateTimeOffset.UtcNow));
        await pending.AddAsync(PendingGoogleSignup.Create(
            Hasher.Hash("signup-" + tag),
            "sub-signup-" + tag,
            Email.Create("novo-" + tag + "@example.com").Value,
            null,
            DateTimeOffset.UtcNow));
        var attempts = new EfAccessAttemptStore(context);
        await attempts.RecordAsync(new AccessAttempt(AccessAction.Login, IpKey, accountKey, Now));
        await attempts.RecordAsync(new AccessAttempt(AccessAction.Login, IpKey, accountKey, Now));
        await new EfUnitOfWork(context).CommitAsync();
        return player.Id;
    }

    private async Task DeletePlayer(PlayerId id, byte[] accountKey, Exception? failAfterCommit = null)
    {
        await using var context = _db.CreateContext();
        var unitOfWork = new EfUnitOfWork(context);
        await unitOfWork.ExecuteAsync(async ct =>
        {
            var players = new EfPlayerRepository(context);
            var player = (await players.GetById(id, ct))!;
            await players.Remove(player, ct);
            await new EfAccessAttemptStore(context).DeleteByAccountAsync(accountKey, ct);
            await unitOfWork.CommitAsync(ct);
            if (failAfterCommit is not null)
            {
                throw failAfterCommit;
            }
        });
    }

    private int Count(string table, string? where = null)
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}" + (where is null ? string.Empty : $" WHERE {where}");
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task REQ_024_deleting_the_player_removes_in_the_same_transaction_every_owned_row_including_the_image()
    {
        var ana = await Populate("ana@example.com", "Ana Souza", "ana", AnaAccountKey);
        foreach (var table in OwnedTables)
        {
            Assert.True(Count(table) > 0, table);
        }

        await DeletePlayer(ana, AnaAccountKey);

        Assert.Equal(0, Count("player"));
        foreach (var table in OwnedTables)
        {
            Assert.Equal(0, Count(table));
        }

        Assert.Equal(0, Count("pending_google_auth", "target_player_id IS NOT NULL"));
    }

    [Fact]
    public async Task REQ_024_pending_requests_targeting_the_player_go_but_the_ones_without_target_stay()
    {
        var ana = await Populate("ana@example.com", "Ana Souza", "ana", AnaAccountKey);

        await DeletePlayer(ana, AnaAccountKey);

        Assert.Equal(1, Count("pending_google_auth"));
        Assert.Equal(1, Count("pending_google_auth", "kind = 'signup'"));
    }

    [Fact]
    public async Task REQ_027_terms_acceptance_and_adult_declaration_vanish_with_the_player_row()
    {
        var ana = await Populate("ana@example.com", "Ana Souza", "ana", AnaAccountKey);
        Assert.Equal(1, Count("player", "terms_version IS NOT NULL AND adult_declared = 1"));

        await DeletePlayer(ana, AnaAccountKey);

        Assert.Equal(0, Count("player", "terms_version IS NOT NULL OR adult_declared = 1"));
    }

    [Fact]
    public async Task REQ_047_access_events_of_the_deleted_account_key_go_and_the_others_stay()
    {
        var ana = await Populate("ana@example.com", "Ana Souza", "ana", AnaAccountKey);
        await Populate("bia@example.com", "Bia Lima", "bia", BiaAccountKey);
        Assert.Equal(4, Count("access_attempt"));

        await DeletePlayer(ana, AnaAccountKey);

        Assert.Equal(2, Count("access_attempt"));
        Assert.Equal(2, Count("access_attempt", "account_key_hash = X'" + Convert.ToHexString(BiaAccountKey) + "'"));
    }

    [Fact]
    public async Task REQ_024_deleting_one_player_leaves_the_data_of_the_other_players_intact()
    {
        var ana = await Populate("ana@example.com", "Ana Souza", "ana", AnaAccountKey);
        await Populate("bia@example.com", "Bia Lima", "bia", BiaAccountKey);

        await DeletePlayer(ana, AnaAccountKey);

        Assert.Equal(1, Count("player"));
        foreach (var table in OwnedTables)
        {
            Assert.True(Count(table) > 0, table);
        }

        Assert.Equal(1, Count("avatar_image", "storage_key = 'key-bia'"));
    }

    [Fact]
    public async Task REQ_025_a_failure_in_the_middle_of_the_deletion_rolls_back_everything()
    {
        var ana = await Populate("ana@example.com", "Ana Souza", "ana", AnaAccountKey);
        var before = OwnedTables.ToDictionary(t => t, t => Count(t));
        var failure = new InvalidOperationException("purger failed");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DeletePlayer(ana, AnaAccountKey, failAfterCommit: failure));

        Assert.Same(failure, thrown);
        Assert.Equal(1, Count("player"));
        Assert.Equal(2, Count("access_attempt"));
        Assert.Equal(2, Count("pending_google_auth"));
        foreach (var (table, count) in before)
        {
            Assert.Equal(count, Count(table));
        }
    }

    [Fact]
    public async Task REQ_048_after_the_deletion_the_email_is_free_and_a_new_signup_gets_another_player_id_without_old_data()
    {
        var ana = await Populate("ana@example.com", "Ana Souza", "ana", AnaAccountKey);
        await DeletePlayer(ana, AnaAccountKey);

        var again = NewPlayer("ana@example.com", "Ana Nova");
        await using (var context = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(context);
            Assert.False(await repository.ExistsByEmail(again.Email));
            await repository.Add(again);
            await new EfUnitOfWork(context).CommitAsync();
        }

        Assert.NotEqual(ana, again.Id);
        Assert.Equal(1, Count("player"));
        foreach (var table in OwnedTables)
        {
            Assert.Equal(0, Count(table));
        }

        Assert.Equal(0, Count("access_attempt"));
    }
}
