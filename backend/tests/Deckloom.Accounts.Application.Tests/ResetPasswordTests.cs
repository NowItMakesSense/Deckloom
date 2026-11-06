using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-036 (REQ-030, 031, 038): confirmacao da redefinicao de senha. RED: os tipos abaixo ainda nao existem.
// Forma esperada (convencao do T-030):
//   record ResetPasswordCommand(string? Token, string? NewPassword)   (campos crus; sem Ip: a confirmacao nao passa pelo AccessThrottle)
//   sealed record PasswordResetCompleted                               (marcador sem campos; "204" sem corpo, design.md linha 1474)
//   sealed class ResetPassword(IPlayerRepository, ISessionRepository, IUnitOfWork, IPasswordHasher, ITokenHasher, TimeProvider)
//     Task<Result<PasswordResetCompleted>> ExecuteAsync(ResetPasswordCommand, CancellationToken ct = default)
// Erros (design.md linha 1474):
//   senha: DomainError("required"|"too_short"|"too_long", "newPassword") -> 400 validation_failed; token nao consumido.
//   token inexistente/expirado/usado/vazio: DomainError("reset_link_invalid") sem campo -> 400 reset_link_invalid.
// REQ-032 cobre so login e solicitacao (AccessAction nao tem confirmacao): sem throttle. Nenhuma sessao e aberta (design.md linha 1490).
public class ResetPasswordTests
{
    private const string RawToken = "reset-token-raw";
    private const string OldPassword = "old password 123";
    private const string NewPassword = "brand new password";
    private const int CurrentIterations = 600_000;

    private static readonly DomainError LinkInvalid = new("reset_link_invalid");

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly SessionRepositorySpy _sessions;
    private readonly FakePasswordHasher _fakeHasher = new(CurrentIterations);
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public ResetPasswordTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new SessionRepositorySpy(new InMemorySessionRepository(_uow), _uow);
    }

    private ResetPassword NewUseCase(IPasswordHasher? hasher = null) =>
        new(_players, _sessions, _uow, hasher ?? _fakeHasher, _tokenHasher, _clock);

    private static ResetPasswordCommand ValidCommand(string? token = RawToken, string? newPassword = NewPassword) =>
        new(token, newPassword);

    private async Task<Player> SeedPlayerWithTokenAsync(
        string email = "ana@example.com", string rawToken = RawToken, DateTimeOffset? issuedAt = null)
    {
        var player = APasswordPlayer(email);
        Assert.True(player.IssuePasswordReset(_tokenHasher.Hash(rawToken), issuedAt ?? Now).IsSuccess);
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    private async Task<Session> SeedSessionAsync(Player player, byte seed)
    {
        var session = ASession(player.Id, seed);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        return session;
    }

    private async Task<string> StoredHashAsync(string password) =>
        (await _fakeHasher.HashAsync(Password.Create(password).Value)).ToStoredString();

    private static string Repeated(int runes) => string.Concat(Enumerable.Repeat("a", runes));

    [Fact]
    public async Task REQ030_valid_token_and_password_replace_the_hash_and_return_the_completed_marker()
    {
        var player = await SeedPlayerWithTokenAsync();
        var oldHash = player.PasswordHash!.ToStoredString();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.IsType<PasswordResetCompleted>(result.Value);
        var stored = (await _players.FindByEmail(AnEmail()))!;
        Assert.Equal(await StoredHashAsync(NewPassword), stored.PasswordHash!.ToStoredString());
        Assert.NotEqual(oldHash, stored.PasswordHash.ToStoredString());
        Assert.Contains(stored.DomainEvents, e => e is PasswordChanged);
    }

    [Fact]
    public async Task REQ030_the_token_is_consumed_so_it_cannot_be_used_again()
    {
        await SeedPlayerWithTokenAsync();
        await NewUseCase().ExecuteAsync(ValidCommand());

        var reuse = await NewUseCase().ExecuteAsync(ValidCommand(newPassword: "another password 1"));

        Assert.Equal(LinkInvalid, Assert.Single(reuse.Errors));
        var stored = (await _players.FindByEmail(AnEmail()))!;
        Assert.Null(stored.PasswordResetToken);
        Assert.Null(await _players.FindByResetTokenHash(_tokenHasher.Hash(RawToken)));
        Assert.Equal(await StoredHashAsync(NewPassword), stored.PasswordHash!.ToStoredString());
    }

    [Fact]
    public async Task REQ030_every_session_of_the_player_is_revoked_with_the_password_reset_reason()
    {
        var player = await SeedPlayerWithTokenAsync();
        var first = await SeedSessionAsync(player, 1);
        var second = await SeedSessionAsync(player, 2);

        await NewUseCase().ExecuteAsync(ValidCommand());

        foreach (var session in new[] { first, second })
        {
            var stored = (await _sessions.FindByTokenHash(session.TokenHash))!;
            Assert.Equal(Now, stored.RevokedAt);
            Assert.Contains(
                stored.DomainEvents,
                e => e is SessionRevoked { Reason: SessionRevocationReason.PasswordReset } revoked
                    && revoked.PlayerId == player.Id);
        }
    }

    [Fact]
    public async Task REQ030_sessions_of_other_players_are_left_untouched()
    {
        await SeedPlayerWithTokenAsync();
        var other = APasswordPlayer("bia@example.com");
        await _players.Add(other);
        await _uow.CommitAsync();
        var otherSession = await SeedSessionAsync(other, 9);

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Null((await _sessions.FindByTokenHash(otherSession.TokenHash))!.RevokedAt);
    }

    [Fact]
    public async Task REQ030_revocation_uses_the_clock_time()
    {
        var player = await SeedPlayerWithTokenAsync();
        var session = await SeedSessionAsync(player, 1);
        _clock.Advance(TimeSpan.FromMinutes(10));

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal(Now.AddMinutes(10), (await _sessions.FindByTokenHash(session.TokenHash))!.RevokedAt);
    }

    [Fact]
    public async Task REQ030_the_reset_and_the_revocation_happen_inside_the_same_unit_of_work_block()
    {
        var player = await SeedPlayerWithTokenAsync();
        await SeedSessionAsync(player, 1);

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal(player.Id, Assert.Single(_sessions.RevokeAllCalls).PlayerId);
        Assert.True(Assert.Single(_sessions.RevokeAllCalls).TransactionWasOpen);
        Assert.True(_uow.TransactionOpenDuringLastCommit);
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task REQ030_the_password_is_hashed_outside_the_unit_of_work_block()
    {
        await SeedPlayerWithTokenAsync();
        var hasher = new ObservingHasher(_fakeHasher, _uow);

        await NewUseCase(hasher).ExecuteAsync(ValidCommand());

        Assert.False(Assert.Single(hasher.TransactionOpenDuringHash));
    }

    [Fact]
    public async Task REQ030_no_session_is_opened_for_the_visitor()
    {
        var player = await SeedPlayerWithTokenAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.Empty(_sessions.AddCalls);
        Assert.Null(await _sessions.FindByTokenHash(AHash(1)));
        Assert.Equal(player.Id, Assert.Single(_sessions.RevokeAllCalls).PlayerId);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(128)]
    public async Task REQ038_passwords_of_8_and_128_characters_are_accepted(int length)
    {
        await SeedPlayerWithTokenAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(newPassword: Repeated(length)));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task REQ038_the_length_limit_counts_unicode_code_points_not_utf16_units()
    {
        await SeedPlayerWithTokenAsync();
        var emojis = string.Concat(Enumerable.Repeat("\U0001F600", 128));

        var result = await NewUseCase().ExecuteAsync(ValidCommand(newPassword: emojis));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task REQ031_a_password_with_7_characters_is_a_too_short_field_error_and_the_token_is_not_consumed()
    {
        var player = await SeedPlayerWithTokenAsync();
        var oldHash = player.PasswordHash!.ToStoredString();
        var hasher = new ObservingHasher(_fakeHasher, _uow);

        var result = await NewUseCase(hasher).ExecuteAsync(ValidCommand(newPassword: Repeated(7)));

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("too_short", "newPassword"), Assert.Single(result.Errors));
        Assert.Empty(hasher.TransactionOpenDuringHash);
        Assert.Empty(_sessions.RevokeAllCalls);
        var stored = (await _players.FindByEmail(AnEmail()))!;
        Assert.NotNull(stored.PasswordResetToken);
        Assert.Equal(oldHash, stored.PasswordHash!.ToStoredString());
        Assert.DoesNotContain(stored.DomainEvents, e => e is PasswordChanged);
    }

    [Fact]
    public async Task REQ038_a_password_with_129_characters_is_a_too_long_field_error_and_the_token_is_not_consumed()
    {
        var player = await SeedPlayerWithTokenAsync();
        var oldHash = player.PasswordHash!.ToStoredString();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(newPassword: Repeated(129)));

        Assert.Equal(new DomainError("too_long", "newPassword"), Assert.Single(result.Errors));
        Assert.Empty(_sessions.RevokeAllCalls);
        var stored = (await _players.FindByEmail(AnEmail()))!;
        Assert.NotNull(stored.PasswordResetToken);
        Assert.Equal(oldHash, stored.PasswordHash!.ToStoredString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task REQ031_an_empty_password_is_a_required_field_error_and_the_token_is_not_consumed(string? password)
    {
        await SeedPlayerWithTokenAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(newPassword: password));

        Assert.Equal(new DomainError("required", "newPassword"), Assert.Single(result.Errors));
        Assert.NotNull((await _players.FindByEmail(AnEmail()))!.PasswordResetToken);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(129)]
    public async Task REQ031_after_a_rejected_password_a_second_valid_submission_with_the_same_token_works(int badLength)
    {
        var player = await SeedPlayerWithTokenAsync();
        await SeedSessionAsync(player, 1);
        await NewUseCase().ExecuteAsync(ValidCommand(newPassword: Repeated(badLength)));

        var retry = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(retry.IsSuccess);
        var stored = (await _players.FindByEmail(AnEmail()))!;
        Assert.Equal(await StoredHashAsync(NewPassword), stored.PasswordHash!.ToStoredString());
        Assert.Null(stored.PasswordResetToken);
        Assert.NotNull((await _sessions.FindByTokenHash(AHash(1)))!.RevokedAt);
    }

    [Fact]
    public async Task REQ031_a_token_just_before_60_minutes_is_still_valid()
    {
        await SeedPlayerWithTokenAsync();
        _clock.Advance(TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(60, 0)]
    [InlineData(60, 1)]
    [InlineData(120, 0)]
    public async Task REQ031_an_expired_token_is_reset_link_invalid_and_changes_nothing(int minutes, int seconds)
    {
        var player = await SeedPlayerWithTokenAsync();
        var oldHash = player.PasswordHash!.ToStoredString();
        var session = await SeedSessionAsync(player, 1);
        _clock.Advance(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsFailure);
        Assert.Equal(LinkInvalid, Assert.Single(result.Errors));
        Assert.Equal(oldHash, (await _players.FindByEmail(AnEmail()))!.PasswordHash!.ToStoredString());
        Assert.Null((await _sessions.FindByTokenHash(session.TokenHash))!.RevokedAt);
    }

    [Fact]
    public async Task REQ031_an_unknown_token_is_reset_link_invalid_and_changes_nothing()
    {
        var player = await SeedPlayerWithTokenAsync();
        var oldHash = player.PasswordHash!.ToStoredString();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(token: "some-other-token"));

        Assert.Equal(LinkInvalid, Assert.Single(result.Errors));
        Assert.Empty(_sessions.RevokeAllCalls);
        var stored = (await _players.FindByEmail(AnEmail()))!;
        Assert.Equal(oldHash, stored.PasswordHash!.ToStoredString());
        Assert.NotNull(stored.PasswordResetToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task REQ031_a_missing_token_is_reset_link_invalid_without_a_field(string? token)
    {
        await SeedPlayerWithTokenAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(token: token));

        Assert.Equal(LinkInvalid, Assert.Single(result.Errors));
        Assert.Null(result.Error.Field);
    }

    [Fact]
    public async Task REQ031_a_token_replaced_by_a_newer_request_is_reset_link_invalid()
    {
        var player = await SeedPlayerWithTokenAsync(rawToken: "first-token");
        Assert.True(player.IssuePasswordReset(_tokenHasher.Hash("second-token"), Now).IsSuccess);
        await _uow.CommitAsync();

        var stale = await NewUseCase().ExecuteAsync(ValidCommand(token: "first-token"));
        var current = await NewUseCase().ExecuteAsync(ValidCommand(token: "second-token"));

        Assert.Equal(LinkInvalid, Assert.Single(stale.Errors));
        Assert.True(current.IsSuccess);
    }

    [Fact]
    public async Task REQ031_all_token_failures_produce_the_same_error_so_no_account_data_is_revealed()
    {
        await SeedPlayerWithTokenAsync();
        await SeedPlayerWithTokenAsync("bia@example.com", "bia-token", Now.AddHours(-2));
        var unknown = await NewUseCase().ExecuteAsync(ValidCommand(token: "ghost"));
        var expired = await NewUseCase().ExecuteAsync(ValidCommand(token: "bia-token"));
        await NewUseCase().ExecuteAsync(ValidCommand());
        var used = await NewUseCase().ExecuteAsync(ValidCommand());

        foreach (var failure in new[] { unknown, expired, used })
        {
            Assert.True(failure.IsFailure);
            Assert.Equal(LinkInvalid, Assert.Single(failure.Errors));
        }
    }

    [Fact]
    public async Task REQ030_the_clear_token_and_password_never_appear_in_domain_events_or_errors()
    {
        await SeedPlayerWithTokenAsync();
        var failure = await NewUseCase().ExecuteAsync(ValidCommand(token: "ghost", newPassword: NewPassword));

        await NewUseCase().ExecuteAsync(ValidCommand());

        var stored = (await _players.FindByEmail(AnEmail()))!;
        var visible = string.Join("|", stored.DomainEvents.Select(e => e.ToString()), failure.Errors);
        Assert.DoesNotContain(RawToken, visible, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, visible, StringComparison.Ordinal);
    }

    [Fact]
    public async Task REQ030_twenty_concurrent_consumptions_of_the_same_token_have_exactly_one_winner()
    {
        const int contenders = 20;
        var player = await SeedPlayerWithTokenAsync();
        await SeedSessionAsync(player, 1);
        var gate = new GateHasher(_fakeHasher, contenders);

        var results = await Task.WhenAll(Enumerable.Range(0, contenders).Select(i =>
            NewUseCase(gate).ExecuteAsync(ValidCommand(newPassword: $"contender password {i:00}"))));

        var winners = results.Select((r, i) => (Result: r, Index: i)).Where(x => x.Result.IsSuccess).ToList();
        var winner = Assert.Single(winners);
        Assert.All(
            results.Where(r => r.IsFailure),
            r => Assert.Equal(LinkInvalid, Assert.Single(r.Errors)));
        var stored = (await _players.FindByEmail(AnEmail()))!;
        Assert.Equal(await StoredHashAsync($"contender password {winner.Index:00}"), stored.PasswordHash!.ToStoredString());
        Assert.Null(stored.PasswordResetToken);
        Assert.Single(stored.DomainEvents, e => e is PasswordChanged);
        Assert.NotNull((await _sessions.FindByTokenHash(AHash(1)))!.RevokedAt);
    }

    // Decorador do repositorio de sessoes: registra se RevokeAllFor/Add rodaram dentro do bloco da unidade de trabalho.
    private sealed class SessionRepositorySpy(InMemorySessionRepository inner, InMemoryUnitOfWork uow) : ISessionRepository
    {
        public List<(PlayerId PlayerId, bool TransactionWasOpen)> RevokeAllCalls { get; } = [];

        public List<Session> AddCalls { get; } = [];

        public async Task Add(Session session, CancellationToken ct = default)
        {
            // Os seeds do teste tambem passam por aqui, fora do bloco; o caso de uso nunca deve adicionar dentro dele.
            if (uow.IsTransactionOpen)
            {
                AddCalls.Add(session);
            }

            await inner.Add(session, ct);
        }

        public Task<Session?> FindById(SessionId id, CancellationToken ct = default) => inner.FindById(id, ct);

        public Task<Session?> FindByTokenHash(TokenHash tokenHash, CancellationToken ct = default) =>
            inner.FindByTokenHash(tokenHash, ct);

        public Task Save(Session session, CancellationToken ct = default) => inner.Save(session, ct);

        public Task<int> RevokeAllFor(
            PlayerId playerId, SessionRevocationReason reason, DateTimeOffset now, CancellationToken ct = default)
        {
            Assert.Equal(SessionRevocationReason.PasswordReset, reason);
            RevokeAllCalls.Add((playerId, uow.IsTransactionOpen));
            return inner.RevokeAllFor(playerId, reason, now, ct);
        }

        public Task<int> DeleteExpiredOrRevoked(DateTimeOffset now, int batchSize, CancellationToken ct = default) =>
            inner.DeleteExpiredOrRevoked(now, batchSize, ct);
    }

    // Decorador do hasher: registra se o hash foi calculado dentro do bloco da unidade de trabalho.
    private sealed class ObservingHasher(FakePasswordHasher inner, InMemoryUnitOfWork uow) : IPasswordHasher
    {
        public List<bool> TransactionOpenDuringHash { get; } = [];

        public ValueTask<PasswordHash> HashAsync(Password password, CancellationToken ct = default)
        {
            TransactionOpenDuringHash.Add(uow.IsTransactionOpen);
            return inner.HashAsync(password, ct);
        }

        public ValueTask<bool> VerifyAsync(Password password, PasswordHash hash, CancellationToken ct = default) =>
            inner.VerifyAsync(password, hash, ct);

        public bool NeedsRehash(PasswordHash hash) => inner.NeedsRehash(hash);
    }

    // Segura cada contendor no hash ate que todos tenham lido o token ainda valido (ou ate o limite de tempo),
    // forcando a disputa real pelo consumo. Continuacoes rodam em linha, uma por vez, sobre os fakes nao thread-safe.
    private sealed class GateHasher(FakePasswordHasher inner, int contenders) : IPasswordHasher
    {
        private readonly TaskCompletionSource _released = new();
        private int _arrived;

        public async ValueTask<PasswordHash> HashAsync(Password password, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _arrived) >= contenders)
            {
                _released.TrySetResult();
            }
            else
            {
                await Task.WhenAny(_released.Task, Task.Delay(TimeSpan.FromSeconds(2), ct));
            }

            return await inner.HashAsync(password, ct);
        }

        public ValueTask<bool> VerifyAsync(Password password, PasswordHash hash, CancellationToken ct = default) =>
            inner.VerifyAsync(password, hash, ct);

        public bool NeedsRehash(PasswordHash hash) => inner.NeedsRehash(hash);
    }
}
