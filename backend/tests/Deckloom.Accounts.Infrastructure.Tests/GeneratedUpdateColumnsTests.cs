using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.ArchitectureTests.Support;
using Deckloom.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-133 (UA1, DB3; ADR-0012, ADR-0015). Os INSERT/UPDATE/DELETE que os adaptadores EF realmente geram so podem tocar o
// que Database/roles/privilege-matrix.md concede a deckloom_app: toda coluna do SET de um UPDATE pertence ao
// UPDATE (...) da tabela; tabela sem UPDATE na matriz nunca recebe UPDATE; INSERT/DELETE so em tabela com `x`; e as
// colunas imutaveis (email, termos, 18+, created_at, id, player_id, token_hash de sessao) nunca entram num SET.
// A matriz e lida pelo unico leitor existente (PrivilegeMatrix, linkado do projeto de testes de arquitetura).
// Limite (ADR-0012): o SQL do SQLite pode diferir do Npgsql (RETURNING, xmin); a conferencia real e H-3/H-8.
// Lacuna: os casos de uso (Application) nao sao exercitados daqui; os fluxos abaixo usam os repositorios, o store e a
// unidade de trabalho que cada caso de uso chama.
public sealed class GeneratedUpdateColumnsTests : IDisposable
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
    private static readonly byte[] IpKey = Enumerable.Repeat((byte)0xA1, 32).ToArray();
    private static readonly byte[] AccountKey = Enumerable.Repeat((byte)0x11, 32).ToArray();

    // Colunas imutaveis para a aplicacao (matriz, Notas). token_hash so e imutavel em player_session.
    private static readonly string[] AlwaysImmutable =
        ["email", "terms_version", "terms_accepted_at", "adult_declared", "created_at", "id", "player_id"];

    private static readonly Dictionary<string, PrivilegeRow> Matrix =
        PrivilegeMatrix.Load(DatabaseDocs.RepoRoot()).ToDictionary(r => r.Table.Split('.')[^1]);

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

    private static void AssertWithinMatrix(IReadOnlyList<CapturedWrite> writes)
    {
        foreach (var write in writes)
        {
            Assert.True(
                Matrix.TryGetValue(write.Table, out var row),
                $"Tabela '{write.Table}' fora da matriz: {write.Sql}");

            switch (write.Kind)
            {
                case WriteKind.Insert:
                    Assert.True(row.Insert, $"INSERT em '{write.Table}', sem x de INSERT na matriz: {write.Sql}");
                    break;
                case WriteKind.Delete:
                    Assert.True(row.Delete, $"DELETE em '{write.Table}', sem x de DELETE na matriz: {write.Sql}");
                    break;
                default:
                    AssertUpdateColumns(write, row);
                    break;
            }
        }
    }

    private static void AssertUpdateColumns(CapturedWrite write, PrivilegeRow row)
    {
        if (row.UpdateWholeTable)
        {
            return;
        }

        Assert.True(
            row.UpdateColumns.Count > 0,
            $"UPDATE em '{write.Table}', que nao tem UPDATE na matriz: {write.Sql}");

        foreach (var column in write.SetColumns)
        {
            var forbidden = AlwaysImmutable.Contains(column)
                || (write.Table == "player_session" && column == "token_hash");
            Assert.False(forbidden, $"UPDATE em '{write.Table}' altera a coluna imutavel '{column}': {write.Sql}");

            // O SQLite nao tem xmin: TestAccountsDbContext regrava uma coluna sombra como token de concorrencia.
            // No PostgreSQL ele so aparece no WHERE; aqui e a unica excecao admitida, so em accounts.player.
            if (write.Table == "player" && column == TestAccountsDbContext.RowVersion)
            {
                continue;
            }

            Assert.True(
                row.UpdateColumns.Contains(column),
                $"UPDATE em '{write.Table}' altera '{column}', fora de UPDATE ({string.Join(", ", row.UpdateColumns)}) da matriz: {write.Sql}");
        }
    }

    private static void AssertWrote(IReadOnlyList<CapturedWrite> writes, WriteKind kind, string table) =>
        Assert.Contains(writes, w => w.Kind == kind && w.Table == table);

    // ---- fluxos ----

    [Fact]
    public void UA1_the_matrix_is_loaded_and_has_the_tables_the_flows_write_to()
    {
        foreach (var table in new[] { "player", "player_session", "password_reset_token", "access_attempt", "pending_google_auth" })
        {
            Assert.True(Matrix.ContainsKey(table), $"Tabela '{table}' ausente da matriz.");
        }

        Assert.Empty(Matrix["pending_google_auth"].UpdateColumns);
        Assert.Contains("password_hash", Matrix["player"].UpdateColumns);
    }

    [Fact]
    public async Task UA1_profile_edit_updates_only_allowed_player_columns_and_replaces_formats_by_insert_and_delete()
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
        AssertWrote(writes, WriteKind.Insert, "player_preferred_format");
        AssertWithinMatrix(writes);
    }

    [Fact]
    public async Task UA1_avatar_change_preset_to_custom_to_preset_updates_only_allowed_columns()
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
            AssertWithinMatrix(writes);
        }
    }

    [Fact]
    public async Task UA1_custom_avatar_image_is_replaced_by_insert_and_delete_never_by_update()
    {
        var saved = await Persist(PasswordPlayer());
        await using (var setup = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(setup);
            var player = (await repository.GetById(saved.Id))!;
            Assert.True(player.SetCustomAvatar($"{saved.Id.Value}/one").IsSuccess);
            await new PostgresAvatarStorage(setup).SaveAsync($"{saved.Id.Value}/one", [1, 2, 3]);
            await new EfUnitOfWork(setup).CommitAsync();
        }

        var writes = await Capture(async context =>
        {
            var repository = new EfPlayerRepository(context);
            var player = (await repository.GetById(saved.Id))!;
            var storage = new PostgresAvatarStorage(context);
            Assert.True(player.SetCustomAvatar($"{saved.Id.Value}/two").IsSuccess);
            await storage.SaveAsync($"{saved.Id.Value}/two", [4, 5, 6]);
            await storage.RemoveAsync($"{saved.Id.Value}/one");
            await new EfUnitOfWork(context).CommitAsync();
        });

        AssertWrote(writes, WriteKind.Insert, "avatar_image");
        AssertWrote(writes, WriteKind.Delete, "avatar_image");
        AssertWithinMatrix(writes);
    }

    [Fact]
    public async Task UA1_password_reset_and_needs_rehash_update_only_allowed_player_columns()
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
        AssertWithinMatrix(rehash);

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
        AssertWithinMatrix(reset);
    }

    [Fact]
    public async Task UA1_issuing_a_reset_token_over_an_existing_one_touches_only_token_columns()
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
        AssertWithinMatrix(writes);
    }

    [Fact]
    public async Task UA1_session_renewal_updates_only_last_seen_and_expiry()
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
        AssertWithinMatrix(writes);
    }

    [Fact]
    public async Task UA1_session_revocation_updates_only_revoked_at()
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
        AssertWithinMatrix(writes);
    }

    [Fact]
    public async Task UA1_revoke_all_for_updates_only_revoked_at_of_each_session()
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
        AssertWithinMatrix(writes);
    }

    [Fact]
    public async Task UA1_google_reauthentication_updates_only_reauthenticated_at()
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
        AssertWithinMatrix(writes);
    }

    [Fact]
    public async Task UA1_consuming_a_pending_google_auth_is_a_delete_and_generates_no_update()
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
        AssertWithinMatrix(writes);
    }

    [Fact]
    public async Task UA1_recording_and_cleaning_access_attempts_only_inserts_and_deletes()
    {
        var writes = await Capture(async context =>
        {
            var store = new EfAccessAttemptStore(context);
            await store.RecordAsync(new AccessAttempt(AccessAction.Login, IpKey, AccountKey, Now));
            await store.RecordAsync(new AccessAttempt(AccessAction.Login, IpKey, AccountKey, Now.AddMinutes(1)));
            await store.DeleteByAccountAsync(AccountKey);
            await new EfUnitOfWork(context).CommitAsync();
            await store.DeleteOlderThanAsync(Now.AddDays(1), 1000);
        });

        AssertWrote(writes, WriteKind.Insert, "access_attempt");
        AssertWrote(writes, WriteKind.Delete, "access_attempt");
        Assert.DoesNotContain(writes, w => w.Kind == WriteKind.Update);
        AssertWithinMatrix(writes);
    }

    [Fact]
    public async Task UA1_cascade_deletion_of_a_player_only_deletes_in_tables_with_delete_in_the_matrix()
    {
        var player = await Persist(PasswordPlayer());
        await using (var setup = _db.CreateContext())
        {
            var repository = new EfPlayerRepository(setup);
            var loaded = (await repository.GetById(player.Id))!;
            Assert.True(loaded.UpdateProfile("Ana Souza", null, "mtg", [Commander, Modern], [Commander, Modern]).IsSuccess);
            Assert.True(loaded.IssuePasswordReset(Hasher.Hash("reset-1"), Now).IsSuccess);
            await new EfUnitOfWork(setup).CommitAsync();
        }

        await Persist(NewSession(player, "token-a"));

        var writes = await Capture(async context =>
        {
            var unitOfWork = new EfUnitOfWork(context);
            await unitOfWork.ExecuteAsync(async ct =>
            {
                var repository = new EfPlayerRepository(context);
                var loaded = (await repository.GetById(player.Id, ct))!;
                await repository.Remove(loaded, ct);
                await new EfAccessAttemptStore(context).DeleteByAccountAsync(AccountKey, ct);
                await unitOfWork.CommitAsync(ct);
            });
        });

        AssertWrote(writes, WriteKind.Delete, "player");
        Assert.DoesNotContain(writes, w => w.Kind == WriteKind.Update);
        AssertWithinMatrix(writes);
    }

    // ---- o detector precisa enxergar uma violacao (anti-vacuo) ----

    [Theory]
    [InlineData("UPDATE \"player\" SET \"email\" = @p0, \"display_name\" = @p1 WHERE \"id\" = @p2", "player", "email,display_name")]
    [InlineData("UPDATE \"accounts\".\"player_session\" SET \"revoked_at\" = @p0 WHERE \"id\" = @p1 RETURNING 1", "player_session", "revoked_at")]
    public void UA1_capture_parses_the_set_columns_of_an_update(string sql, string table, string columns)
    {
        var capture = new UpdateStatementCapture();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        capture.NonQueryExecuting(command, null!, default);

        var write = Assert.Single(capture.Writes);
        Assert.Equal(WriteKind.Update, write.Kind);
        Assert.Equal(table, write.Table);
        Assert.Equal(columns.Split(','), write.SetColumns);
    }

    // Prova do vermelho (T-133): DbSet.Update num grafo desanexado gera SET de todas as colunas, inclusive email.
    [Fact]
    public async Task UA1_dbset_update_on_a_detached_player_is_caught_because_it_sets_email()
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
        Assert.ThrowsAny<Exception>(() => AssertWithinMatrix(writes));
    }

    [Fact]
    public void UA1_a_whole_row_update_with_email_is_rejected_by_the_rule()
    {
        var write = new CapturedWrite(
            WriteKind.Update,
            "player",
            ["email", "display_name"],
            "UPDATE \"player\" SET \"email\" = @p0, \"display_name\" = @p1 WHERE \"id\" = @p2");

        Assert.ThrowsAny<Exception>(() => AssertWithinMatrix([write]));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
