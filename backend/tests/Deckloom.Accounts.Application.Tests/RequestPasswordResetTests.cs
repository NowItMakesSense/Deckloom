using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-035 (REQ-028, 029, 032): solicitacao de redefinicao de senha. RED: os tipos abaixo ainda nao existem.
// Forma esperada (convencao do T-030):
//   record RequestPasswordResetCommand(string? Email, string Ip)
//   sealed record PasswordResetRequestAccepted  (marcador sem campos; resultado generico unico, "202 sempre", design.md linha 1456)
//   sealed record PasswordResetOptions { string PublicBaseUrl { get; init; } }  (Application; config "Auth:PasswordReset:PublicBaseUrl")
//   sealed class RequestPasswordReset(IPlayerRepository, IUnitOfWork, ISecureTokenGenerator, ITokenHasher,
//       IPasswordResetNotifier, AccessThrottle, PasswordResetOptions, TimeProvider)
//     Task<Result<PasswordResetRequestAccepted>> ExecuteAsync(RequestPasswordResetCommand, CancellationToken ct = default)
// Unico erro: email vazio/mal formado -> DomainError("required"|"invalid_format", "email") -> 400 validation_failed (design.md linha 1456).
// O atraso minimo de 500 ms da resposta (design.md linha 1472) e da camada de API, nao deste caso de uso.
public class RequestPasswordResetTests
{
    private const string Ip = "203.0.113.7";
    private const string RawEmail = "ana@example.com";
    private const string BaseUrl = "https://app.deckloom.test";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly SpyAttemptStore _attempts;
    private readonly SequentialTokenGenerator _tokens = new();
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeKeyedHasher _keyedHasher = new();
    private readonly RecordingDelayer _delayer = new();
    private readonly RecordingPasswordResetNotifier _notifier = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public RequestPasswordResetTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _attempts = new SpyAttemptStore(new InMemoryAccessAttemptStore(_uow));
    }

    private AccessThrottle NewThrottle() =>
        new(_attempts, _keyedHasher, _delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock);

    private RequestPasswordReset NewUseCase(IPasswordResetNotifier? notifier = null, string baseUrl = BaseUrl) =>
        new(
            _players,
            _uow,
            _tokens,
            _tokenHasher,
            notifier ?? _notifier,
            NewThrottle(),
            new PasswordResetOptions { PublicBaseUrl = baseUrl },
            _clock);

    private static RequestPasswordResetCommand ValidCommand(string? email = RawEmail, string ip = Ip) =>
        new(email, ip);

    private async Task<Player> SeedPasswordPlayerAsync(string email = RawEmail)
    {
        var player = APasswordPlayer(email);
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    private async Task SeedGooglePlayerAsync(string email = "gui@example.com")
    {
        await _players.Add(AGooglePlayer(email, "sub-1"));
        await _uow.CommitAsync();
    }

    private async Task SeedRequestsAsync(string email, string ip, int count)
    {
        var throttle = NewThrottle();
        for (var i = 0; i < count; i++)
        {
            // SEC-01: o evento provisorio gravado por BeforeAttemptAsync ja conta a solicitacao.
            await throttle.BeforeAttemptAsync(AccessAction.PasswordResetRequest, email, ip);
        }

        _attempts.Recorded.Clear();
        _delayer.Delays.Clear();
    }

    private Task<int> AccountEventsAsync(string email = RawEmail) =>
        _attempts.CountByAccountAsync(_keyedHasher.Hash(email), Now.AddHours(-1));

    private Task<int> IpEventsAsync(string ip = Ip) =>
        _attempts.CountByIpAsync(_keyedHasher.Hash(ip), Now.AddHours(-1));

    [Fact]
    public async Task REQ028_password_account_gets_a_token_whose_sha256_is_stored_and_the_clear_token_goes_only_to_the_link()
    {
        await SeedPasswordPlayerAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        var stored = (await _players.FindByEmail(AnEmail()))!;
        Assert.NotNull(stored.PasswordResetToken);
        Assert.True(_tokenHasher.Hash("tok-0001").Equals(stored.PasswordResetToken!.TokenHash));
        Assert.NotNull(await _players.FindByResetTokenHash(_tokenHasher.Hash("tok-0001")));
        Assert.Equal(Now.AddMinutes(60), stored.PasswordResetToken.ExpiresAt);
    }

    [Fact]
    public async Task REQ028_the_link_is_enqueued_for_the_account_email_with_the_token_in_the_fragment()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        var sent = Assert.Single(_notifier.Sent);
        Assert.Equal(AnEmail(), sent.Email);
        Assert.Equal($"{BaseUrl}/redefinir-senha#token=tok-0001", sent.Link);
    }

    [Fact]
    public async Task REQ028_email_is_matched_ignoring_case_and_surrounding_spaces()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand("  ANA@Example.com "));

        Assert.Single(_notifier.Sent);
    }

    [Fact]
    public async Task REQ028_a_trailing_slash_on_the_public_base_url_does_not_double_the_separator()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase(baseUrl: BaseUrl + "/").ExecuteAsync(ValidCommand());

        Assert.Equal($"{BaseUrl}/redefinir-senha#token=tok-0001", Assert.Single(_notifier.Sent).Link);
    }

    [Fact]
    public async Task REQ028_public_base_url_comes_from_the_options()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase(baseUrl: "https://outro.exemplo").ExecuteAsync(ValidCommand());

        Assert.Equal("https://outro.exemplo/redefinir-senha#token=tok-0001", Assert.Single(_notifier.Sent).Link);
    }

    [Fact]
    public async Task REQ029_a_new_request_invalidates_the_previous_token()
    {
        await SeedPasswordPlayerAsync();
        await NewUseCase().ExecuteAsync(ValidCommand());

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Null(await _players.FindByResetTokenHash(_tokenHasher.Hash("tok-0001")));
        Assert.NotNull(await _players.FindByResetTokenHash(_tokenHasher.Hash("tok-0002")));
        Assert.Equal(2, _notifier.Sent.Count);
        Assert.EndsWith("#token=tok-0002", _notifier.Sent[1].Link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task REQ029_the_token_is_persisted_inside_a_unit_of_work_block()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(_uow.TransactionOpenDuringLastCommit);
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task REQ029_the_email_is_only_enqueued_after_the_token_is_committed()
    {
        await SeedPasswordPlayerAsync();
        var observing = new CommitObservingNotifier(_players, _tokenHasher, _notifier);

        await NewUseCase(observing).ExecuteAsync(ValidCommand());

        Assert.True(observing.TokenWasCommittedWhenNotified);
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task REQ028_unknown_email_returns_the_generic_result_and_sends_nothing()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand("ghost@example.com"));

        Assert.True(result.IsSuccess);
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public async Task REQ028_google_only_account_returns_the_generic_result_and_sends_nothing()
    {
        await SeedGooglePlayerAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand("gui@example.com"));

        Assert.True(result.IsSuccess);
        Assert.Empty(_notifier.Sent);
        Assert.Null((await _players.FindByEmail(AnEmail("gui@example.com")))!.PasswordResetToken);
    }

    [Fact]
    public async Task REQ028_notifier_failure_returns_the_generic_result_without_revealing_it()
    {
        await SeedPasswordPlayerAsync();
        _notifier.FailWith(new InvalidOperationException("smtp down for ana@example.com"));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public async Task REQ028_the_result_is_identical_for_existing_unknown_google_only_and_notifier_failure()
    {
        await SeedPasswordPlayerAsync();
        await SeedGooglePlayerAsync();
        var existing = await NewUseCase().ExecuteAsync(ValidCommand());
        var unknown = await NewUseCase().ExecuteAsync(ValidCommand("ghost@example.com"));
        var googleOnly = await NewUseCase().ExecuteAsync(ValidCommand("gui@example.com"));
        _notifier.FailWith(new InvalidOperationException("boom"));
        var failing = await NewUseCase().ExecuteAsync(ValidCommand());

        foreach (var other in new[] { unknown, googleOnly, failing })
        {
            Assert.True(other.IsSuccess);
            Assert.IsType<PasswordResetRequestAccepted>(other.Value);
            Assert.Equal(existing.Value, other.Value);
        }
    }

    [Fact]
    public async Task REQ028_empty_email_is_a_required_validation_error_and_sends_nothing()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand(""));

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("required", "email"), Assert.Single(result.Errors));
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public async Task REQ028_null_email_is_a_required_validation_error()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand(null));

        Assert.Equal(new DomainError("required", "email"), Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ028_malformed_email_is_an_invalid_format_validation_error_and_sends_nothing()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand("not-an-email"));

        Assert.Equal(new DomainError("invalid_format", "email"), Assert.Single(result.Errors));
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public async Task REQ032_every_valid_request_records_one_password_reset_event_for_the_account_and_ip_keys()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal(1, await AccountEventsAsync());
        Assert.Equal(1, await IpEventsAsync());
        Assert.Equal(AccessAction.PasswordResetRequest, Assert.Single(_attempts.Recorded).Action);
    }

    [Fact]
    public async Task SEC01_validation_errors_release_the_provisional_event()
    {
        await NewUseCase().ExecuteAsync(ValidCommand("not-an-email"));
        await NewUseCase().ExecuteAsync(ValidCommand(""));

        Assert.Equal(0, await AccountEventsAsync("not-an-email"));
        Assert.Equal(0, await IpEventsAsync());
    }

    [Fact]
    public async Task SEC01_over_the_account_budget_answers_the_same_acceptance_but_issues_no_token_and_sends_no_email()
    {
        await SeedPasswordPlayerAsync();
        await SeedRequestsAsync(RawEmail, "198.51.100.1", 30);

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.Empty(_notifier.Sent);
        var stored = (await _players.FindByEmail(AnEmail()))!;
        Assert.Null(stored.PasswordResetToken);
        Assert.Equal(30, await AccountEventsAsync()); // a recusa nao consome orcamento
    }

    [Fact]
    public async Task SEC01_over_the_budget_gives_the_same_answer_for_an_account_that_does_not_exist()
    {
        await SeedRequestsAsync("ghost@example.com", "198.51.100.1", 30);

        var result = await NewUseCase().ExecuteAsync(ValidCommand("ghost@example.com"));

        Assert.True(result.IsSuccess);
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public async Task SEC03_repeated_requests_for_one_account_send_at_most_the_hourly_budget_of_emails()
    {
        await SeedPasswordPlayerAsync();

        for (var i = 0; i < 45; i++)
        {
            Assert.True((await NewUseCase().ExecuteAsync(ValidCommand(ip: $"198.51.100.{i}"))).IsSuccess);
        }

        Assert.Equal(30, _notifier.Sent.Count);
        Assert.Equal(30, await AccountEventsAsync());
    }

    [Fact]
    public async Task REQ032_unknown_email_is_recorded_on_the_same_keys_so_existence_is_not_revealed()
    {
        await NewUseCase().ExecuteAsync(ValidCommand("ghost@example.com"));

        Assert.Equal(1, await AccountEventsAsync("ghost@example.com"));
        Assert.Equal(1, await IpEventsAsync());
        Assert.Equal(AccessAction.PasswordResetRequest, Assert.Single(_attempts.Recorded).Action);
    }

    [Fact]
    public async Task REQ032_google_only_account_is_recorded()
    {
        await SeedGooglePlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand("gui@example.com"));

        Assert.Equal(1, await AccountEventsAsync("gui@example.com"));
        Assert.Equal(1, await IpEventsAsync());
    }

    [Fact]
    public async Task REQ032_request_is_recorded_even_when_the_notifier_fails()
    {
        await SeedPasswordPlayerAsync();
        _notifier.FailWith(new InvalidOperationException("smtp down"));

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal(1, await AccountEventsAsync());
        Assert.Equal(1, await IpEventsAsync());
    }

    [Fact]
    public async Task REQ032_repeated_requests_keep_accumulating_because_success_never_clears_them()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());
        await NewUseCase().ExecuteAsync(ValidCommand());
        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal(3, await AccountEventsAsync());
        Assert.Equal(3, await IpEventsAsync());
    }

    [Fact]
    public async Task REQ032_no_delay_is_applied_on_the_first_request()
    {
        await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Empty(_delayer.Delays);
    }

    [Fact]
    public async Task REQ032_progressive_delay_is_applied_from_the_account_requests_and_the_request_still_succeeds()
    {
        await SeedPasswordPlayerAsync();
        await SeedRequestsAsync(RawEmail, "198.51.100.1", new ProgressiveDelayOptions().AccountThreshold);

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(_delayer.Delays));
        Assert.Single(_notifier.Sent);
    }

    [Fact]
    public async Task REQ032_progressive_delay_is_applied_from_the_ip_requests()
    {
        await SeedRequestsAsync("other@example.com", Ip, new ProgressiveDelayOptions().IpThreshold);

        await NewUseCase().ExecuteAsync(ValidCommand("ghost@example.com"));

        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(_delayer.Delays));
    }

    [Fact]
    public async Task REQ032_unknown_email_gets_the_same_delay_as_an_existing_one_for_the_same_request_count()
    {
        await SeedRequestsAsync("ghost@example.com", "198.51.100.1", new ProgressiveDelayOptions().AccountThreshold);

        await NewUseCase().ExecuteAsync(ValidCommand("ghost@example.com"));

        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(_delayer.Delays));
    }

    [Fact]
    public async Task REQ028_the_clear_token_never_appears_in_the_stored_state_or_in_domain_events()
    {
        var player = await SeedPasswordPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());
        await NewUseCase().ExecuteAsync(ValidCommand());

        var stored = (await _players.FindByEmail(AnEmail()))!;
        var visible = string.Join(
            "|",
            stored.DomainEvents.Select(e => e.ToString()),
            player.DomainEvents.Select(e => e.ToString()),
            stored.PasswordResetToken!.TokenHash.ToString());
        Assert.DoesNotContain("tok-0001", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("tok-0002", visible, StringComparison.Ordinal);
        Assert.Contains(stored.DomainEvents, e => e is PasswordResetIssued);
    }

    // Decorador que guarda cada tentativa registrada (o fake nao expoe a acao) e delega ao fake.
    private sealed class SpyAttemptStore(InMemoryAccessAttemptStore inner) : IAccessAttemptStore
    {
        public List<AccessAttempt> Recorded { get; } = [];

        public Task<long> RecordAsync(AccessAttempt attempt, CancellationToken ct = default)
        {
            Recorded.Add(attempt);
            return inner.RecordAsync(attempt, ct);
        }

        public Task<int> DeleteByIdAsync(long id, CancellationToken ct = default) => inner.DeleteByIdAsync(id, ct);

        public Task<int> CountByIpAsync(byte[] ipKeyHash, DateTimeOffset since, CancellationToken ct = default) =>
            inner.CountByIpAsync(ipKeyHash, since, ct);

        public Task<int> CountByAccountAsync(byte[] accountKeyHash, DateTimeOffset since, CancellationToken ct = default) =>
            inner.CountByAccountAsync(accountKeyHash, since, ct);

        public Task<int> DeleteByAccountAsync(byte[] accountKeyHash, CancellationToken ct = default) =>
            inner.DeleteByAccountAsync(accountKeyHash, ct);

        public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct = default) =>
            inner.DeleteOlderThanAsync(cutoff, batchSize, ct);
    }

    // Decorador que, no momento do envio, confere se o token ja esta confirmado no repositorio.
    private sealed class CommitObservingNotifier(
        InMemoryPlayerRepository players, Sha256TokenHasher hasher, RecordingPasswordResetNotifier inner)
        : IPasswordResetNotifier
    {
        public bool TokenWasCommittedWhenNotified { get; private set; }

        public async Task NotifyAsync(PlayerId playerId, Email email, string resetLink, CancellationToken ct = default)
        {
            var token = resetLink[(resetLink.IndexOf("#token=", StringComparison.Ordinal) + "#token=".Length)..];
            TokenWasCommittedWhenNotified = await players.FindByResetTokenHash(hasher.Hash(token), ct) is not null;
            await inner.NotifyAsync(playerId, email, resetLink, ct);
        }
    }
}
