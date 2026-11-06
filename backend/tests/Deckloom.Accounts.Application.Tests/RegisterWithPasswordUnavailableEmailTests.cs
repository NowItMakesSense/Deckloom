using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-032 (REQ-002, 027, 041): cadastro com senha recusado por email indisponivel.
// Forma esperada do erro (design.md linha 1406 e 1373): Result.Errors com um unico DomainError("unavailable", "email")
// (mapeado na API para 409 email_unavailable, errors.email=["unavailable"]); nunca excecao, inclusive na corrida
// em que a violacao de unicidade (ux_player_email) estoura no commit (design.md linhas 297, 331, 1463).
// A recusa grava um evento Registration so com a chave de IP (AccountKeyHash nulo, REQ-041, design.md linha 1406).
public class RegisterWithPasswordUnavailableEmailTests
{
    private const string Ip = "203.0.113.7";
    private const string TermsVersion = "2026-10-01";
    private const string RawEmail = "ana@example.com";
    private const string RawPassword = "correct horse battery";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly InMemoryAccessAttemptStore _attempts;
    private readonly FakePasswordHasher _hasher = new(600_000);
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

    private readonly FixedTermsVersionProvider _terms = new(
        TermsVersion, new Uri("https://deckloom.test/termos"), new Uri("https://deckloom.test/privacidade"));

    public RegisterWithPasswordUnavailableEmailTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _attempts = new InMemoryAccessAttemptStore(_uow);
    }

    private static readonly DomainError Unavailable = new("unavailable", "email");

    private AccessThrottle NewThrottle() =>
        new(_attempts, _keyedHasher, _delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock);

    private RegisterWithPassword NewUseCase(IPlayerRepository? players = null) =>
        new(players ?? _players, _sessions, _uow, _hasher, _tokens, _tokenHasher, _terms, _catalog, NewThrottle(), _clock);

    private static RegisterWithPasswordCommand ValidCommand(string displayName = "Bia", string ip = Ip) =>
        new(RawEmail, RawPassword, displayName, true, TermsVersion, true, ip);

    private async Task<Player> SeedExistingPlayerAsync()
    {
        var existing = APasswordPlayer(RawEmail);
        await _players.Add(existing);
        await _uow.CommitAsync();
        return existing;
    }

    [Fact]
    public async Task REQ002_existing_email_is_refused_with_unavailable_on_the_email_field()
    {
        await SeedExistingPlayerAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsFailure);
        Assert.Equal(Unavailable, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ002_existing_email_creates_no_player_and_no_session_and_leaves_the_account_untouched()
    {
        var existing = await SeedExistingPlayerAsync();
        var hashBefore = existing.PasswordHash!.ToStoredString();

        await NewUseCase().ExecuteAsync(ValidCommand());

        var stored = await _players.FindByEmail(AnEmail(RawEmail));
        Assert.NotNull(stored);
        Assert.Equal(existing.Id, stored.Id);
        Assert.Equal("Ana", stored.DisplayName.Value);
        Assert.Equal(hashBefore, stored.PasswordHash!.ToStoredString());
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
    }

    [Fact]
    public async Task REQ002_email_comparison_ignores_case_and_surrounding_spaces()
    {
        await SeedExistingPlayerAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { Email = "  ANA@Example.com " });

        Assert.Equal(Unavailable, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ041_refusal_records_one_registration_event_with_the_ip_key_only()
    {
        await SeedExistingPlayerAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        var since = Now.AddHours(-1);
        Assert.Equal(1, await _attempts.CountByIpAsync(_keyedHasher.Hash(Ip), since));
        Assert.Equal(0, await _attempts.CountByAccountAsync(_keyedHasher.Hash(RawEmail), since));
    }

    [Fact]
    public async Task REQ041_REQ032_refusal_does_not_touch_the_account_key_so_a_legitimate_login_is_not_delayed()
    {
        await SeedExistingPlayerAsync();
        for (var i = 0; i < 5; i++)
        {
            await NewUseCase().ExecuteAsync(ValidCommand(ip: $"198.51.100.{i}"));
        }

        // SEC-01: antes do login, a chave de conta esta limpa; o login passa a gravar so o proprio provisorio.
        Assert.Equal(0, await _attempts.CountByAccountAsync(_keyedHasher.Hash(RawEmail), Now.AddHours(-1)));
        var delaysBefore = _delayer.Delays.Count;
        await NewThrottle().BeforeAttemptAsync(AccessAction.Login, RawEmail, "192.0.2.50");

        Assert.Equal(delaysBefore, _delayer.Delays.Count);
    }

    [Fact]
    public async Task REQ041_ip_with_several_refusals_gets_a_growing_delay_and_is_never_blocked()
    {
        await SeedExistingPlayerAsync();
        var thresholdAttempts = new ProgressiveDelayOptions().IpThreshold;
        for (var i = 0; i < thresholdAttempts; i++)
        {
            await NewUseCase().ExecuteAsync(ValidCommand());
        }

        Assert.Empty(_delayer.Delays);

        var first = await NewUseCase().ExecuteAsync(ValidCommand());
        var second = await NewUseCase().ExecuteAsync(ValidCommand());
        var third = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal(3, _delayer.Delays.Count);
        Assert.True(_delayer.Delays[1] > _delayer.Delays[0]);
        Assert.True(_delayer.Delays[2] > _delayer.Delays[1]);
        Assert.All(new[] { first, second, third }, r => Assert.Equal(Unavailable, Assert.Single(r.Errors)));
    }

    [Fact]
    public async Task REQ041_refusals_of_one_ip_do_not_delay_a_different_ip()
    {
        await SeedExistingPlayerAsync();
        var thresholdAttempts = new ProgressiveDelayOptions().IpThreshold + 2;
        for (var i = 0; i < thresholdAttempts; i++)
        {
            await NewUseCase().ExecuteAsync(ValidCommand());
        }

        var delaysBefore = _delayer.Delays.Count;
        var other = await NewUseCase().ExecuteAsync(
            ValidCommand(ip: "198.51.100.9") with { Email = "nova@example.com" });

        Assert.True(other.IsSuccess);
        Assert.Equal(delaysBefore, _delayer.Delays.Count);
    }

    [Fact]
    public async Task REQ027_email_of_a_deleted_account_registers_a_brand_new_player_without_previous_data()
    {
        var old = await SeedExistingPlayerAsync();
        await _players.Remove(old);
        await _uow.CommitAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(displayName: "Bia"));

        Assert.True(result.IsSuccess);
        var created = await _players.FindByEmail(AnEmail(RawEmail));
        Assert.NotNull(created);
        Assert.NotEqual(old.Id, created.Id);
        Assert.Equal("Bia", created.DisplayName.Value);
        Assert.IsType<PresetAvatar>(created.Avatar);
        Assert.Equal("preset-02", ((PresetAvatar)created.Avatar).PresetId);
        Assert.NotEqual(old.PasswordHash!.ToStoredString(), created.PasswordHash!.ToStoredString());
        Assert.Equal(FormatPreferences.Empty, created.Formats);
        var session = await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001"));
        Assert.Equal(created.Id, session?.PlayerId);
    }

    [Fact]
    public async Task REQ002_REQ041_concurrent_registrations_of_the_same_email_one_wins_and_the_other_is_refused_without_exception()
    {
        // Ambos leem "email livre" (leitura defasada) e so o commit detecta a violacao de ux_player_email.
        var staleReads = new StaleReadPlayerRepository(_players);

        var winner = await NewUseCase(staleReads).ExecuteAsync(ValidCommand(displayName: "Bia"));
        var loser = await NewUseCase(staleReads).ExecuteAsync(ValidCommand(displayName: "Caio", ip: "198.51.100.9"));

        Assert.True(winner.IsSuccess);
        Assert.True(loser.IsFailure);
        Assert.Equal(Unavailable, Assert.Single(loser.Errors));
    }

    [Fact]
    public async Task REQ002_concurrent_loser_leaves_nothing_persisted_and_the_winner_keeps_the_account()
    {
        var staleReads = new StaleReadPlayerRepository(_players);
        var winner = await NewUseCase(staleReads).ExecuteAsync(ValidCommand(displayName: "Bia"));

        await NewUseCase(staleReads).ExecuteAsync(ValidCommand(displayName: "Caio", ip: "198.51.100.9"));

        var stored = await _players.FindByEmail(AnEmail(RawEmail));
        Assert.Equal("Bia", stored!.DisplayName.Value);
        Assert.NotNull(await _sessions.FindByTokenHash(_tokenHasher.Hash(winner.Value.SessionToken)));
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0002")));
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task REQ041_concurrent_loser_counts_as_a_refusal_for_its_ip_only()
    {
        var staleReads = new StaleReadPlayerRepository(_players);
        await NewUseCase(staleReads).ExecuteAsync(ValidCommand(displayName: "Bia"));
        var loserIp = "198.51.100.9";

        await NewUseCase(staleReads).ExecuteAsync(ValidCommand(displayName: "Caio", ip: loserIp));

        var since = Now.AddHours(-1);
        Assert.Equal(1, await _attempts.CountByIpAsync(_keyedHasher.Hash(loserIp), since));
        Assert.Equal(0, await _attempts.CountByIpAsync(_keyedHasher.Hash(Ip), since));
        Assert.Equal(0, await _attempts.CountByAccountAsync(_keyedHasher.Hash(RawEmail), since));
    }

    // Simula a corrida: leituras de existencia nao enxergam o commit do concorrente; escritas vao ao repositorio real.
    private sealed class StaleReadPlayerRepository(IPlayerRepository inner) : IPlayerRepository
    {
        public Task<Player?> GetById(PlayerId id, CancellationToken ct = default) => inner.GetById(id, ct);

        public Task<Player?> FindByEmail(Email email, CancellationToken ct = default) => Task.FromResult<Player?>(null);

        public Task<Player?> FindByGoogleSubject(string subject, CancellationToken ct = default) =>
            inner.FindByGoogleSubject(subject, ct);

        public Task<Player?> FindByResetTokenHash(TokenHash tokenHash, CancellationToken ct = default) =>
            inner.FindByResetTokenHash(tokenHash, ct);

        public Task<bool> ExistsByEmail(Email email, CancellationToken ct = default) => Task.FromResult(false);

        public Task Add(Player player, CancellationToken ct = default) => inner.Add(player, ct);

        public Task Save(Player player, CancellationToken ct = default) => inner.Save(player, ct);

        public Task Remove(Player player, CancellationToken ct = default) => inner.Remove(player, ct);

        public Task<int> DeleteExpiredResetTokens(DateTimeOffset now, int batchSize, CancellationToken ct = default) =>
            inner.DeleteExpiredResetTokens(now, batchSize, ct);
    }
}
