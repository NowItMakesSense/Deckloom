using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Deckloom.Accounts.Infrastructure.Tests;

// FEAT-COLUNAS-IMUTAVEIS (REQ-I01; ADR-0012). Os UPDATE que os adaptadores EF realmente geram nunca podem pôr no SET
// uma coluna imutavel. A lista fica aqui, no proprio teste (sem matriz de privilegios), e o teste falha se uma coluna
// da lista deixar de existir no modelo EF. INSERT/DELETE nao sao julgados aqui.
// Limite (ADR-0012): o SQL do SQLite pode diferir do Npgsql (RETURNING, xmin); a conferencia real e H-3/H-8.
// Lacuna: os casos de uso (Application) nao sao exercitados daqui; os fluxos abaixo usam os repositorios, o store e a
// unidade de trabalho que cada caso de uso chama.
public sealed class ImmutableColumnsTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";
    private const string NewStored =
        "v1$pbkdf2-sha256$600000$b3V0cm9zYWx0b3V0cm8=$b3V0cm9oYXNob3V0cm9oYXNob3V0cm9oYXNob3V0cm8=";

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Sha256TokenHasher Hasher = new();
    private static readonly FormatId Commander = new("commander");
    private static readonly FormatId Modern = new("modern");
    private static readonly FormatId Pauper = new("pauper");

    // Colunas imutaveis por tabela (nomes reais do modelo EF). token_hash so e imutavel em player_session: em
    // password_reset_token ele e regravado ao reemitir o token. pending_google_auth nunca recebe UPDATE.
    private static readonly Dictionary<string, string[]> Immutable = new()
    {
        ["player"] = ["id", "email", "terms_version", "terms_accepted_at", "adult_declared", "created_at"],
        ["player_session"] = ["id", "player_id", "token_hash", "created_at"],
        ["password_reset_token"] = ["player_id"],
        ["pending_google_auth"] = ["id", "email", "created_at"],
    };

    private readonly SqliteInMemoryFixture _db = new();

    public void Dispose() => _db.Dispose();

    // ---- preparacao (fora da captura) ----

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

    private async Task<Player> Persist(Player player)
    {
        _db.SeedFormats();
        _db.SeedAvatarPresets("preset-1", "preset-2");
        await using var context = _db.CreateContext();
        await new EfPlayerRepository(context).Add(player);
        await new EfUnitOfWork(context).CommitAsync();
        return player;
    }

    private async Task Persist(Session session)
    {
        await using var context = _db.CreateContext();
        await new EfSessionRepository(context).Add(session);
        await new EfUnitOfWork(context).CommitAsync();
    }

    private static Session NewSession(Player player, string token, bool rememberMe = false) =>
        Session.Start(player.Id, Hasher.Hash(token), rememberMe, Now);

    // ---- captura ----

    private async Task<IReadOnlyList<CapturedWrite>> Capture(Func<AccountsDbContext, Task> flow)
    {
        var capture = new UpdateStatementCapture();
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseSqlite(_db.Connection)
            .AddInterceptors(capture)
            .Options;
        await using var context = new TestAccountsDbContext(options);
        await flow(context);
        return capture.Writes;
    }

    // ---- regra ----

    private static void AssertNoImmutableColumnInSet(IReadOnlyList<CapturedWrite> writes)
    {
        foreach (var write in writes.Where(w => w.Kind == WriteKind.Update))
        {
            if (!Immutable.TryGetValue(write.Table, out var immutable))
            {
                continue;
            }

            foreach (var column in write.SetColumns)
            {
                Assert.False(
                    immutable.Contains(column),
                    $"UPDATE em '{write.Table}' altera a coluna imutavel '{column}': {write.Sql}");
            }
        }
    }

    // Colunas listadas que nao existem no modelo EF (tabela/coluna reais, inclusive tipos "owned" da mesma tabela).
    private static List<string> MissingFromModel(IModel model, Dictionary<string, string[]> listed)
    {
        var existing = new HashSet<string>();
        foreach (var entity in model.GetEntityTypes())
        {
            var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table);
            if (table is null)
            {
                continue;
            }

            foreach (var property in entity.GetProperties())
            {
                existing.Add($"{table.Value.Name}.{property.GetColumnName(table.Value)}");
            }
        }

        return listed
            .SelectMany(t => t.Value.Select(c => $"{t.Key}.{c}"))
            .Where(c => !existing.Contains(c))
            .ToList();
    }

    private static void AssertWrote(IReadOnlyList<CapturedWrite> writes, WriteKind kind, string table) =>
        Assert.Contains(writes, w => w.Kind == kind && w.Table == table);

    // ---- lista x modelo EF ----

    [Fact]
    public void Every_listed_immutable_column_exists_in_the_ef_model()
    {
        using var context = _db.CreateContext();

        Assert.Empty(MissingFromModel(context.Model, Immutable));
    }

    [Fact]
    public void A_listed_column_missing_from_the_ef_model_is_reported()
    {
        using var context = _db.CreateContext();
        var listed = new Dictionary<string, string[]>
        {
            ["player"] = ["email", "coluna_inexistente"],
            ["tabela_inexistente"] = ["id"],
        };

        Assert.Equal(["player.coluna_inexistente", "tabela_inexistente.id"], MissingFromModel(context.Model, listed));
    }

    // ---- fluxos ----

    [Fact]
    public async Task Profile_edit_does_not_set_immutable_player_columns()
    {
        var saved = await Persist(PasswordPlayer());
        await using (var setup = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(setup);
            var player = (await repository.GetById(saved.Id))!;
            Assert.True(player.UpdateProfile("Ana Souza", null, "mtg", [Commander], [Commander, Modern, Pauper]).IsSuccess);
            await repository.Save(player);
            await new EfUnitOfWork(setup).CommitAsync();
        }

        var writes = await Capture(async context =>
        {
            var repository = new EfPlayerRepository(context);
            var player = (await repository.GetById(saved.Id))!;
            Assert.True(player.UpdateProfile("Ana Nova", "Nova bio", "mtg", [Modern, Pauper], [Commander, Modern, Pauper]).IsSuccess);
            await repository.Save(player);
            await new EfUnitOfWork(context).CommitAsync();
        });

        AssertWrote(writes, WriteKind.Update, "player");
        AssertNoImmutableColumnInSet(writes);
    }

    [Fact]
    public async Task Avatar_change_preset_to_custom_to_preset_does_not_set_immutable_player_columns()
    {
        var saved = await Persist(PasswordPlayer());
        var steps = new Func<Player, Result<Player>>[]
        {
            p => p.SetCustomAvatar("avatar-key-1"),
            p => p.ChooseAvatarPreset("preset-2"),
        };

        foreach (var step in steps)
        {
            var writes = await Capture(async context =>
            {
                var repository = new EfPlayerRepository(context);
                var player = (await repository.GetById(saved.Id))!;
                Assert.True(step(player).IsSuccess);
                await repository.Save(player);
                await new EfUnitOfWork(context).CommitAsync();
            });

            AssertWrote(writes, WriteKind.Update, "player");
            AssertNoImmutableColumnInSet(writes);
        }
    }

    [Fact]
    public async Task Password_reset_and_needs_rehash_do_not_set_immutable_player_columns()
    {
        var saved = await Persist(PasswordPlayer());

        var rehash = await Capture(async context =>
        {
            var repository = new EfPlayerRepository(context);
            var player = (await repository.GetById(saved.Id))!;
            Assert.True(player.RehashPassword(PasswordHash.Parse(NewStored).Value).IsSuccess);
            await repository.Save(player);
            await new EfUnitOfWork(context).CommitAsync();
        });
        AssertWrote(rehash, WriteKind.Update, "player");
        AssertNoImmutableColumnInSet(rehash);

        await using (var issue = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(issue);
            var player = (await repository.GetById(saved.Id))!;
            Assert.True(player.IssuePasswordReset(Hasher.Hash("reset-1"), Now).IsSuccess);
            await new EfUnitOfWork(issue).CommitAsync();
        }

        var reset = await Capture(async context =>
        {
            var repository = new EfPlayerRepository(context);
            var player = (await repository.FindByResetTokenHash(Hasher.Hash("reset-1")))!;
            Assert.True(player.ResetPassword(Hasher.Hash("reset-1"), PasswordHash.Parse(Stored).Value, Now.AddMinutes(5)).IsSuccess);
            await repository.Save(player);
            await new EfUnitOfWork(context).CommitAsync();
        });
        AssertWrote(reset, WriteKind.Update, "player");
        AssertWrote(reset, WriteKind.Delete, "password_reset_token");
        AssertNoImmutableColumnInSet(reset);
    }

    [Fact]
    public async Task Issuing_a_reset_token_over_an_existing_one_does_not_set_immutable_columns()
    {
        var saved = await Persist(PasswordPlayer());
        await using (var first = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(first);
            var player = (await repository.GetById(saved.Id))!;
            Assert.True(player.IssuePasswordReset(Hasher.Hash("reset-1"), Now).IsSuccess);
            await new EfUnitOfWork(first).CommitAsync();
        }

        var writes = await Capture(async context =>
        {
            var repository = new EfPlayerRepository(context);
            var player = (await repository.GetById(saved.Id))!;
            Assert.True(player.IssuePasswordReset(Hasher.Hash("reset-2"), Now.AddMinutes(10)).IsSuccess);
            await repository.Save(player);
            await new EfUnitOfWork(context).CommitAsync();
        });

        Assert.Contains(writes, w => w.Table == "password_reset_token");
        AssertNoImmutableColumnInSet(writes);
    }

    [Fact]
    public async Task Session_renewal_does_not_set_immutable_session_columns()
    {
        var player = await Persist(PasswordPlayer());
        var session = NewSession(player, "token-a");
        await Persist(session);

        var writes = await Capture(async context =>
        {
            var repository = new EfSessionRepository(context);
            var loaded = (await repository.FindByTokenHash(session.TokenHash))!;
            Assert.True(loaded.Touch(Now.AddMinutes(10)));
            await repository.Save(loaded);
            await new EfUnitOfWork(context).CommitAsync();
        });

        AssertWrote(writes, WriteKind.Update, "player_session");
        AssertNoImmutableColumnInSet(writes);
    }

    [Fact]
    public async Task Session_revocation_does_not_set_immutable_session_columns()
    {
        var player = await Persist(PasswordPlayer());
        var session = NewSession(player, "token-a");
        await Persist(session);

        var writes = await Capture(async context =>
        {
            var repository = new EfSessionRepository(context);
            var loaded = (await repository.FindByTokenHash(session.TokenHash))!;
            loaded.Revoke(SessionRevocationReason.Logout, Now.AddMinutes(5));
            await repository.Save(loaded);
            await new EfUnitOfWork(context).CommitAsync();
        });

        AssertWrote(writes, WriteKind.Update, "player_session");
        AssertNoImmutableColumnInSet(writes);
    }

    [Fact]
    public async Task Revoke_all_for_does_not_set_immutable_session_columns()
    {
        var player = await Persist(PasswordPlayer());
        await Persist(NewSession(player, "ana-1"));
        await Persist(NewSession(player, "ana-2", rememberMe: true));

        var writes = await Capture(async context =>
        {
            var count = await new EfSessionRepository(context)
                .RevokeAllFor(player.Id, SessionRevocationReason.PasswordReset, Now.AddMinutes(3));
            Assert.Equal(2, count);
            await new EfUnitOfWork(context).CommitAsync();
        });

        AssertWrote(writes, WriteKind.Update, "player_session");
        AssertNoImmutableColumnInSet(writes);
    }

    [Fact]
    public async Task Google_reauthentication_does_not_set_immutable_session_columns()
    {
        var player = await Persist(PasswordPlayer());
        var session = NewSession(player, "token-a");
        await Persist(session);
        var identity = ExternalIdentity.Create("google", "sub-1").Value;

        var writes = await Capture(async context =>
        {
            var repository = new EfSessionRepository(context);
            var loaded = (await repository.FindByTokenHash(session.TokenHash))!;
            Assert.True(loaded.MarkReauthenticated(identity, identity, Now.AddMinutes(1)).IsSuccess);
            await repository.Save(loaded);
            await new EfUnitOfWork(context).CommitAsync();
        });

        AssertWrote(writes, WriteKind.Update, "player_session");
        AssertNoImmutableColumnInSet(writes);
    }

    [Fact]
    public async Task Consuming_a_pending_google_auth_deletes_and_sets_no_immutable_column()
    {
        var token = Hasher.Hash("pending-1");
        await using (var setup = _db.CreateContext())
        {
            var store = new EfPendingGoogleAuthStore(setup, new FixedClock(Now));
            await store.AddAsync(PendingGoogleSignup.Create(token, "sub-1", Email.Create("gui@example.com").Value, "Gui", Now));
            await new EfUnitOfWork(setup).CommitAsync();
        }

        var writes = await Capture(async context =>
        {
            var consumed = await new EfPendingGoogleAuthStore(context, new FixedClock(Now.AddMinutes(1))).ConsumeAsync(token);
            Assert.NotNull(consumed);
        });

        AssertWrote(writes, WriteKind.Delete, "pending_google_auth");
        Assert.DoesNotContain(writes, w => w.Kind == WriteKind.Update);
        AssertNoImmutableColumnInSet(writes);
    }

    // ---- o detector precisa enxergar uma violacao (anti-vacuo) ----

    // Prova do vermelho: DbSet.Update num grafo desanexado gera SET de todas as colunas, inclusive email.
    [Fact]
    public async Task Dbset_update_on_a_detached_player_is_caught_because_it_sets_email()
    {
        var saved = await Persist(PasswordPlayer());
        Player detached;
        await using (var read = _db.CreateContext())
        {
            detached = (await new EfPlayerRepository(read).GetById(saved.Id))!;
        }

        var writes = await Capture(async context =>
        {
            context.Players.Update(detached);

            // O token sombra do SQLite nao viaja no grafo desanexado: o UPDATE e emitido (e capturado) e pode afetar 0 linhas.
            await Assert.ThrowsAnyAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
        });

        Assert.Contains(writes, w => w.Kind == WriteKind.Update && w.Table == "player" && w.SetColumns.Contains("email"));
        Assert.ThrowsAny<Exception>(() => AssertNoImmutableColumnInSet(writes));
    }

    [Theory]
    [InlineData("player", "email")]
    [InlineData("player", "terms_version")]
    [InlineData("player", "terms_accepted_at")]
    [InlineData("player", "adult_declared")]
    [InlineData("player", "created_at")]
    [InlineData("player", "id")]
    [InlineData("player_session", "token_hash")]
    [InlineData("player_session", "player_id")]
    [InlineData("player_session", "id")]
    [InlineData("player_session", "created_at")]
    [InlineData("password_reset_token", "player_id")]
    [InlineData("pending_google_auth", "email")]
    public void An_update_that_sets_a_listed_immutable_column_is_rejected_by_the_rule(string table, string column)
    {
        var write = new CapturedWrite(
            WriteKind.Update,
            table,
            ["revoked_at", column],
            $"UPDATE \"{table}\" SET \"revoked_at\" = @p0, \"{column}\" = @p1 WHERE \"id\" = @p2");

        Assert.ThrowsAny<Exception>(() => AssertNoImmutableColumnInSet([write]));
    }

    [Fact]
    public void An_update_that_sets_only_mutable_columns_passes_the_rule()
    {
        var write = new CapturedWrite(
            WriteKind.Update,
            "password_reset_token",
            ["token_hash", "issued_at", "expires_at"],
            "UPDATE \"password_reset_token\" SET \"token_hash\" = @p0 WHERE \"player_id\" = @p1");

        AssertNoImmutableColumnInSet([write]);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
