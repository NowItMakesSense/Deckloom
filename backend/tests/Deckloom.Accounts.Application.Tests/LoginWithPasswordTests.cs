using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-033 (REQ-004, 005, 027, 032, 035, 038): login com senha. RED: os tipos abaixo ainda nao existem.
// Forma esperada (convencao do T-030, ver relatorio do test agent):
//   record LoginWithPasswordCommand(string? Email, string? Password, bool RememberMe, string Ip)
//   sealed class LoginWithPassword(IPlayerRepository, ISessionRepository, IUnitOfWork, IPasswordHasher,
//       ISecureTokenGenerator, ITokenHasher, IAvatarPresetCatalog, AccessThrottle, TimeProvider)
//     Task<Result<AuthenticatedSession>> ExecuteAsync(LoginWithPasswordCommand, CancellationToken ct = default)
// Erro unico para toda falha de credencial: DomainError("invalid_credentials") sem campo (design.md linhas 1407, 1431).
// Campos vazios: DomainError("required", "email" | "password") -> 400 validation_failed (design.md linha 1407).
public class LoginWithPasswordTests
{
    private const string Ip = "203.0.113.7";
    private const string RawEmail = "ana@example.com";
    private const string RawPassword = "correct horse battery";
    private const int CurrentIterations = 600_000;

    private static readonly DomainError InvalidCredentials = new("invalid_credentials");

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly InMemoryAccessAttemptStore _attempts;
    private readonly FakePasswordHasher _hasher = new(CurrentIterations);
    private readonly SequentialTokenGenerator _tokens = new();
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeKeyedHasher _keyedHasher = new();
    private readonly RecordingDelayer _delayer = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    public LoginWithPasswordTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _attempts = new InMemoryAccessAttemptStore(_uow);
    }

    private AccessThrottle NewThrottle() =>
        new(_attempts, _keyedHasher, _delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock);

    private LoginWithPassword NewUseCase(IPasswordHasher? hasher = null) =>
        new(_players, _sessions, _uow, hasher ?? _hasher, _tokens, _tokenHasher, _catalog, NewThrottle(), _clock);

    private static LoginWithPasswordCommand ValidCommand(
        string email = RawEmail, string password = RawPassword, bool rememberMe = false, string ip = Ip) =>
        new(email, password, rememberMe, ip);

    private static async Task<PasswordHash> HashWithAsync(int iterations, string password) =>
        await new FakePasswordHasher(iterations).HashAsync(Password.Create(password).Value);

    private async Task<Player> SeedPasswordPlayerAsync(int iterations = CurrentIterations, string email = RawEmail)
    {
        var player = Player.RegisterWithPassword(
            AnEmail(email),
            await HashWithAsync(iterations, RawPassword),
            DisplayName.Create("Ana").Value,
            "2026-10-01",
            "2026-10-01",
            true,
            true,
            Now,
            "preset-01").Value;
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    private async Task SeedFailuresAsync(string email, string ip, int count)
    {
        var throttle = NewThrottle();
        for (var i = 0; i < count; i++)
        {
            // SEC-01: o evento provisorio gravado por BeforeAttemptAsync ja e a falha.
            await throttle.BeforeAttemptAsync(AccessAction.Login, email, ip);
        }
    }

    private Task<int> AccountFailuresAsync(string email = RawEmail) =>
        _attempts.CountByAccountAsync(_keyedHasher.Hash(email), Now.AddHours(-1));

    private Task<int> IpFailuresAsync(string ip = Ip) =>
        _attempts.CountByIpAsync(_keyedHasher.Hash(ip), Now.AddHours(-1));

    [Fact]
    public async Task REQ004_correct_credentials_open_a_session_and_return_the_summary()
    {
        var player = await SeedPasswordPlayerAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        var session = await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001"));
        Assert.NotNull(session);
        Assert.Equal(player.Id, session!.PlayerId);
        Assert.Equal("tok-0001", result.Value.SessionToken);
        Assert.Equal(session.ExpiresAt, result.Value.ExpiresAt);
        Assert.Equal("Ana", result.Value.Summary.DisplayName);
        Assert.True(result.Value.Summary.HasPassword);
        Assert.False(result.Value.Summary.HasGoogle);
        Assert.Equal("preset-01", result.Value.Summary.Avatar.PresetId);
    }

    [Fact]
    public async Task REQ004_email_is_matched_ignoring_case_and_surrounding_spaces()
    {
        await SeedPasswordPlayerAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(email: "  ANA@Example.com "));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task REQ035_every_login_issues_a_fresh_token_so_sessions_are_never_reused_or_fixated()
    {
        await SeedPasswordPlayerAsync();

        var first = await NewUseCase().ExecuteAsync(ValidCommand());
        var second = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.NotEqual(first.Value.SessionToken, second.Value.SessionToken);
        var firstSession = await _sessions.FindByTokenHash(_tokenHasher.Hash(first.Value.SessionToken));
        var secondSession = await _sessions.FindByTokenHash(_tokenHasher.Hash(second.Value.SessionToken));
        Assert.NotNull(firstSession);
        Assert.NotNull(secondSession);
        Assert.NotEqual(firstSession!.Id, secondSession!.Id);
    }

    [Fact]
    public async Task REQ035_session_stores_only_the_token_hash_never_the_raw_token()
    {
        await SeedPasswordPlayerAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("not-the-token")));
        var session = await _sessions.FindByTokenHash(_tokenHasher.Hash(result.Value.SessionToken));
        Assert.True(_tokenHasher.Hash(result.Value.SessionToken).Equals(session!.TokenHash));
    }

    [Fact]
    public async Task REQ004_without_remember_me_the_session_lasts_30_minutes()
    {
        await SeedPasswordPlayerAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(rememberMe: false));

        var session = await _sessions.FindByTokenHash(_tokenHasher.Hash(result.Value.SessionToken));
        Assert.False(session!.RememberMe);
        Assert.Equal(Now.AddMinutes(30), session.ExpiresAt);
        Assert.Equal(Now.AddMinutes(30), result.Value.ExpiresAt);
    }

    [Fact]
    public async Task REQ027_with_remember_me_the_session_lasts_30_days()
    {
        await SeedPasswordPlayerAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(rememberMe: true));

        var session = await _sessions.FindByTokenHash(_tokenHasher.Hash(result.Value.SessionToken));
        Assert.True(session!.RememberMe);
        Assert.Equal(Now.AddDays(30), session.ExpiresAt);
        Assert.Equal(Now.AddDays(30), result.Value.ExpiresAt);
    }

    [Fact]
    public async Task REQ004_session_is_persisted_inside_a_unit_of_work_block()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(_uow.TransactionOpenDuringLastCommit);
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task REQ004_wrong_password_is_refused_with_invalid_credentials_and_opens_no_session()
    {
        await SeedPasswordPlayerAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(password: "wrong password here"));

        Assert.True(result.IsFailure);
        Assert.Equal(InvalidCredentials, Assert.Single(result.Errors));
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
    }

    [Fact]
    public async Task REQ005_unknown_email_gets_the_same_invalid_credentials()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand(email: "ghost@example.com"));

        Assert.Equal(InvalidCredentials, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ005_google_only_account_gets_the_same_invalid_credentials()
    {
        await _players.Add(AGooglePlayer("gui@example.com", "sub-1"));
        await _uow.CommitAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(email: "gui@example.com"));

        Assert.Equal(InvalidCredentials, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ005_deleted_account_gets_the_same_invalid_credentials()
    {
        var player = await SeedPasswordPlayerAsync();
        await _players.Remove(player);
        await _uow.CommitAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal(InvalidCredentials, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ005_malformed_email_gets_the_same_invalid_credentials_not_a_validation_error()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand(email: "not-an-email"));

        Assert.Equal(InvalidCredentials, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ005_wrong_password_calls_the_hasher_exactly_once_and_it_fails()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand(password: "wrong password here"));

        var call = Assert.Single(_hasher.VerifyCalls);
        Assert.False(call.Result);
    }

    [Fact]
    public async Task REQ005_unknown_email_verifies_once_against_a_dummy_hash_to_equalize_time()
    {
        var other = await SeedPasswordPlayerAsync(email: "other@example.com");

        await NewUseCase().ExecuteAsync(ValidCommand(email: "ghost@example.com"));

        var call = Assert.Single(_hasher.VerifyCalls);
        Assert.False(call.Result);
        Assert.Equal(RawPassword, call.Password.Reveal());
        Assert.NotEqual(other.PasswordHash!.ToStoredString(), call.Hash.ToStoredString());
    }

    [Fact]
    public async Task REQ005_google_only_account_verifies_once_against_a_dummy_hash_to_equalize_time()
    {
        await _players.Add(AGooglePlayer("gui@example.com", "sub-1"));
        await _uow.CommitAsync();

        await NewUseCase().ExecuteAsync(ValidCommand(email: "gui@example.com"));

        var call = Assert.Single(_hasher.VerifyCalls);
        Assert.False(call.Result);
    }

    [Fact]
    public async Task REQ005_deleted_account_verifies_once_against_a_dummy_hash_to_equalize_time()
    {
        var player = await SeedPasswordPlayerAsync();
        await _players.Remove(player);
        await _uow.CommitAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        var call = Assert.Single(_hasher.VerifyCalls);
        Assert.False(call.Result);
    }

    [Fact]
    public async Task REQ005_malformed_email_verifies_once_against_a_dummy_hash_to_equalize_time()
    {
        await NewUseCase().ExecuteAsync(ValidCommand(email: "not-an-email"));

        var call = Assert.Single(_hasher.VerifyCalls);
        Assert.False(call.Result);
    }

    [Fact]
    public async Task REQ004_correct_login_verifies_exactly_once()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        var call = Assert.Single(_hasher.VerifyCalls);
        Assert.True(call.Result);
    }

    [Fact]
    public async Task REQ038_password_over_128_characters_is_invalid_credentials_without_calling_the_hasher()
    {
        await SeedPasswordPlayerAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(password: new string('a', 129)));

        Assert.Equal(InvalidCredentials, Assert.Single(result.Errors));
        Assert.Empty(_hasher.VerifyCalls);
        Assert.Empty(_hasher.HashCalls);
        Assert.Empty(_hasher.NeedsRehashCalls);
    }

    [Fact]
    public async Task REQ038_password_over_128_characters_is_counted_and_delayed_like_any_other_failure()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand(password: new string('a', 129)));

        Assert.Equal(1, await AccountFailuresAsync());
        Assert.Equal(1, await IpFailuresAsync());
    }

    [Fact]
    public async Task REQ004_password_shorter_than_the_minimum_can_never_match_and_is_invalid_credentials_without_the_hasher()
    {
        await SeedPasswordPlayerAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(password: "short"));

        Assert.Equal(InvalidCredentials, Assert.Single(result.Errors));
        Assert.Empty(_hasher.VerifyCalls);
    }

    [Fact]
    public async Task REQ004_empty_fields_are_a_validation_error_per_field_and_never_reach_the_hasher()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand(email: "", password: ""));

        Assert.True(result.IsFailure);
        Assert.Contains(new DomainError("required", "email"), result.Errors);
        Assert.Contains(new DomainError("required", "password"), result.Errors);
        Assert.Empty(_hasher.VerifyCalls);
    }

    [Fact]
    public async Task REQ004_null_email_is_a_required_validation_error()
    {
        var result = await NewUseCase().ExecuteAsync(new LoginWithPasswordCommand(null, RawPassword, false, Ip));

        Assert.Equal(new DomainError("required", "email"), Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ032_failure_records_one_login_event_for_the_account_and_ip_keys()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand(password: "wrong password here"));

        Assert.Equal(1, await AccountFailuresAsync());
        Assert.Equal(1, await IpFailuresAsync());
    }

    [Fact]
    public async Task REQ032_failure_for_an_unknown_email_is_recorded_on_the_same_keys_so_existence_is_not_revealed()
    {
        await NewUseCase().ExecuteAsync(ValidCommand(email: "ghost@example.com"));

        Assert.Equal(1, await AccountFailuresAsync("ghost@example.com"));
        Assert.Equal(1, await IpFailuresAsync());
    }

    [Fact]
    public async Task REQ032_success_records_no_failure_event()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal(0, await AccountFailuresAsync());
        Assert.Equal(0, await IpFailuresAsync());
        Assert.Empty(_delayer.Delays);
    }

    [Fact]
    public async Task REQ032_progressive_delay_is_applied_from_the_account_failures_even_for_the_correct_password()
    {
        await SeedPasswordPlayerAsync();
        await SeedFailuresAsync(RawEmail, "198.51.100.1", new ProgressiveDelayOptions().AccountThreshold);

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(_delayer.Delays));
    }

    [Fact]
    public async Task REQ032_the_delay_is_applied_before_any_credential_verification()
    {
        await SeedPasswordPlayerAsync();
        await SeedFailuresAsync(RawEmail, "198.51.100.1", new ProgressiveDelayOptions().AccountThreshold);
        var observing = new DelayObservingHasher(_hasher, _delayer);

        await NewUseCase(observing).ExecuteAsync(ValidCommand());

        Assert.Equal(1, observing.DelaysSeenAtVerify);
    }

    [Fact]
    public async Task REQ032_unknown_email_gets_the_same_delay_as_an_existing_one_for_the_same_failure_count()
    {
        await SeedFailuresAsync("ghost@example.com", "198.51.100.1", new ProgressiveDelayOptions().AccountThreshold);

        await NewUseCase().ExecuteAsync(ValidCommand(email: "ghost@example.com"));

        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(_delayer.Delays));
    }

    [Fact]
    public async Task REQ032_success_clears_the_account_failures_but_keeps_other_accounts()
    {
        await SeedPasswordPlayerAsync();
        await SeedFailuresAsync(RawEmail, "198.51.100.1", 2);
        await SeedFailuresAsync("other@example.com", "198.51.100.2", 2);

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal(0, await AccountFailuresAsync());
        Assert.Equal(2, await AccountFailuresAsync("other@example.com"));
    }

    [Fact]
    public async Task REQ032_failure_does_not_clear_the_account_failures()
    {
        await SeedPasswordPlayerAsync();
        await SeedFailuresAsync(RawEmail, "198.51.100.1", 2);

        await NewUseCase().ExecuteAsync(ValidCommand(password: "wrong password here"));

        Assert.Equal(3, await AccountFailuresAsync());
    }

    [Fact]
    public async Task REQ032_clearing_failures_happens_inside_the_session_unit_of_work_block()
    {
        await SeedPasswordPlayerAsync();
        await SeedFailuresAsync(RawEmail, "198.51.100.1", 2);

        await NewUseCase().ExecuteAsync(ValidCommand());

        // O fake so aplica DeleteByAccountAsync no commit: se ficasse fora do ExecuteAsync, as falhas permaneceriam.
        Assert.True(_uow.TransactionOpenDuringLastCommit);
        Assert.Equal(0, await AccountFailuresAsync());
        Assert.NotNull(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
    }

    [Fact]
    public async Task REQ038_hash_with_outdated_parameters_is_rewritten_after_a_successful_login()
    {
        var player = await SeedPasswordPlayerAsync(iterations: 100_000);
        var oldStored = player.PasswordHash!.ToStoredString();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        var stored = (await _players.FindByEmail(AnEmail(RawEmail)))!;
        var rehashed = Assert.Single(_hasher.HashCalls);
        Assert.Equal(RawPassword, rehashed.Reveal());
        Assert.NotEqual(oldStored, stored.PasswordHash!.ToStoredString());
        Assert.Equal((await _hasher.HashAsync(rehashed)).ToStoredString(), stored.PasswordHash.ToStoredString());
        Assert.False(_hasher.NeedsRehash(stored.PasswordHash));
    }

    [Fact]
    public async Task REQ038_current_hash_is_not_rewritten()
    {
        var player = await SeedPasswordPlayerAsync();
        var before = player.PasswordHash!.ToStoredString();

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Empty(_hasher.HashCalls);
        Assert.Equal(before, (await _players.FindByEmail(AnEmail(RawEmail)))!.PasswordHash!.ToStoredString());
    }

    [Fact]
    public async Task REQ038_failed_login_never_rewrites_the_hash_even_when_outdated()
    {
        var player = await SeedPasswordPlayerAsync(iterations: 100_000);
        var before = player.PasswordHash!.ToStoredString();

        await NewUseCase().ExecuteAsync(ValidCommand(password: "wrong password here"));

        Assert.Empty(_hasher.HashCalls);
        Assert.Equal(before, (await _players.FindByEmail(AnEmail(RawEmail)))!.PasswordHash!.ToStoredString());
    }

    [Fact]
    public async Task SEC01_empty_fields_release_the_provisional_event_so_validation_errors_are_not_counted()
    {
        await NewUseCase().ExecuteAsync(ValidCommand(email: "", password: ""));

        Assert.Equal(0, await AccountFailuresAsync(""));
        Assert.Equal(0, await IpFailuresAsync());
    }

    [Fact]
    public async Task SEC01_a_failed_credential_is_counted_exactly_once_by_the_provisional_event()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand(password: "wrong password here"));
        await NewUseCase().ExecuteAsync(ValidCommand(password: "short"));

        Assert.Equal(2, await AccountFailuresAsync());
        Assert.Equal(2, await IpFailuresAsync());
    }

    [Fact]
    public async Task SEC01_over_the_account_budget_is_refused_with_too_many_attempts_without_verifying_the_password()
    {
        await SeedPasswordPlayerAsync();
        await SeedFailuresAsync(RawEmail, "198.51.100.1", 30);

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("too_many_attempts"), Assert.Single(result.Errors));
        Assert.Empty(_hasher.VerifyCalls);
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
        Assert.Equal(30, await AccountFailuresAsync()); // a recusa nao consome orcamento
    }

    [Fact]
    public async Task SEC01_over_the_budget_gives_the_same_answer_for_an_account_that_does_not_exist()
    {
        await SeedFailuresAsync("ghost@example.com", "198.51.100.1", 30);

        var existing = await NewUseCase().ExecuteAsync(ValidCommand(email: "ghost@example.com"));

        Assert.Equal(new DomainError("too_many_attempts"), Assert.Single(existing.Errors));
        Assert.Empty(_hasher.VerifyCalls);
    }

    [Fact]
    public async Task SEC01_parallel_logins_on_one_account_from_many_ips_verify_the_password_at_most_thirty_times()
    {
        await SeedPasswordPlayerAsync();
        var counting = new CountingHasher(_hasher);
        var delayer = new ThreadSafeDelayer();
        var useCase = new LoginWithPassword(
            _players,
            _sessions,
            _uow,
            counting,
            _tokens,
            _tokenHasher,
            _catalog,
            new AccessThrottle(
                _attempts, _keyedHasher, delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock),
            _clock);

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(i =>
            Task.Run(() => useCase.ExecuteAsync(ValidCommand(password: "wrong password here", ip: $"198.51.100.{i}")))));

        Assert.True(counting.Verifications <= 30);
        Assert.Equal(100, results.Length);
        Assert.All(results, r => Assert.True(r.IsFailure));
        Assert.Contains(results, r => r.Errors.Single() == new DomainError("too_many_attempts"));
        Assert.Equal(TimeSpan.FromSeconds(10), delayer.Snapshot().Max());
    }

    private sealed class CountingHasher(FakePasswordHasher inner) : IPasswordHasher
    {
        private int _verifications;

        public int Verifications => Volatile.Read(ref _verifications);

        public ValueTask<PasswordHash> HashAsync(Password password, CancellationToken ct = default) =>
            inner.HashAsync(password, ct);

        public ValueTask<bool> VerifyAsync(Password password, PasswordHash hash, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _verifications);
            return ValueTask.FromResult(false);
        }

        public bool NeedsRehash(PasswordHash hash) => false;
    }

    private sealed class ThreadSafeDelayer : IDelayer
    {
        private readonly object _lock = new();
        private readonly List<TimeSpan> _delays = [];

        public IReadOnlyList<TimeSpan> Snapshot()
        {
            lock (_lock)
            {
                return [.. _delays];
            }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
        {
            lock (_lock)
            {
                _delays.Add(delay);
            }

            return Task.CompletedTask;
        }
    }

    // Decorador que registra quantos atrasos ja tinham sido aplicados quando a verificacao comecou.
    private sealed class DelayObservingHasher(FakePasswordHasher inner, RecordingDelayer delayer) : IPasswordHasher
    {
        public int? DelaysSeenAtVerify { get; private set; }

        public ValueTask<PasswordHash> HashAsync(Password password, CancellationToken ct = default) =>
            inner.HashAsync(password, ct);

        public ValueTask<bool> VerifyAsync(Password password, PasswordHash hash, CancellationToken ct = default)
        {
            DelaysSeenAtVerify = delayer.Delays.Count;
            return inner.VerifyAsync(password, hash, ct);
        }

        public bool NeedsRehash(PasswordHash hash) => inner.NeedsRehash(hash);
    }
}
