using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-030 (REQ-001, 022, 033, 044, 048): caminho feliz do cadastro com senha. RED: os tipos abaixo ainda nao existem.
// Forma esperada (a confirmar com o usuario, ver relatorio do test agent):
//   record RegisterWithPasswordCommand(string? Email, string? Password, string? DisplayName,
//       bool AcceptTerms, string? TermsVersion, bool AdultDeclaration, string Ip)
//   sealed class RegisterWithPassword(IPlayerRepository, ISessionRepository, IUnitOfWork, IPasswordHasher,
//       ISecureTokenGenerator, ITokenHasher, ITermsVersionProvider, IAvatarPresetCatalog, AccessThrottle, TimeProvider)
//     Task<Result<AuthenticatedSession>> ExecuteAsync(RegisterWithPasswordCommand, CancellationToken ct = default)
//   record AuthenticatedSession(string SessionToken, DateTimeOffset ExpiresAt, SessionSummary Summary)
//   record SessionSummary(string DisplayName, AvatarView Avatar, bool HasPassword, bool HasGoogle)
//   record AvatarView(string Kind, string? PresetId, string ImageUrl, string Alt)
public class RegisterWithPasswordTests
{
    private const string Ip = "203.0.113.7";
    private const string TermsVersion = "2026-10-01";
    private const string RawEmail = "ana@example.com";
    private const string RawPassword = "correct horse battery";
    private const string DefaultPresetId = "preset-02";

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
        new AvatarPresetEntry(DefaultPresetId, "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    private readonly FixedTermsVersionProvider _terms = new(
        TermsVersion, new Uri("https://deckloom.test/termos"), new Uri("https://deckloom.test/privacidade"));

    public RegisterWithPasswordTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _attempts = new InMemoryAccessAttemptStore(_uow);
    }

    private AccessThrottle NewThrottle() =>
        new(_attempts, _keyedHasher, _delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock);

    private RegisterWithPassword NewUseCase(ISecureTokenGenerator? tokens = null) =>
        new(_players, _sessions, _uow, _hasher, tokens ?? _tokens, _tokenHasher, _terms, _catalog, NewThrottle(), _clock);

    private static RegisterWithPasswordCommand ValidCommand() =>
        new(RawEmail, RawPassword, "Ana", true, TermsVersion, true, Ip);

    private async Task<Player> StoredPlayer() =>
        (await _players.FindByEmail(AnEmail(RawEmail)))!;

    [Fact]
    public async Task REQ001_valid_registration_succeeds_and_creates_the_player_without_asking_for_email_verification()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        var player = await StoredPlayer();
        Assert.NotNull(player);
        Assert.Equal("Ana", player.DisplayName.Value);
        Assert.Equal(AnEmail(RawEmail), player.Email);
    }

    [Fact]
    public async Task REQ001_stores_only_the_password_hash_and_never_the_plaintext()
    {
        await NewUseCase().ExecuteAsync(ValidCommand());

        var player = await StoredPlayer();
        var hashCall = Assert.Single(_hasher.HashCalls);
        Assert.Equal(RawPassword, hashCall.Reveal());
        Assert.NotNull(player.PasswordHash);
        Assert.DoesNotContain(RawPassword, player.PasswordHash!.ToStoredString());
        Assert.True(await _hasher.VerifyAsync(Password.Create(RawPassword).Value, player.PasswordHash!));
    }

    [Fact]
    public async Task REQ022_new_player_gets_the_catalog_default_preset_avatar()
    {
        await NewUseCase().ExecuteAsync(ValidCommand());

        var player = await StoredPlayer();
        var avatar = Assert.IsType<PresetAvatar>(player.Avatar);
        Assert.Equal(DefaultPresetId, avatar.PresetId);
    }

    [Fact]
    public async Task REQ044_new_player_has_magic_as_favorite_tcg_and_no_formats()
    {
        await NewUseCase().ExecuteAsync(ValidCommand());

        var player = await StoredPlayer();
        Assert.Equal(FavoriteTcg.Default, player.FavoriteTcg);
        Assert.Equal(FormatPreferences.Empty, player.Formats);
    }

    [Fact]
    public async Task REQ033_REQ048_records_the_terms_acceptance_with_version_time_and_adult_declaration()
    {
        _clock.Advance(TimeSpan.FromMinutes(5));

        await NewUseCase().ExecuteAsync(ValidCommand());

        var terms = (await StoredPlayer()).TermsAcceptance;
        Assert.Equal(TermsVersion, terms.TermsVersion);
        Assert.Equal(Now.AddMinutes(5), terms.AcceptedAt);
        Assert.True(terms.AdultDeclared);
    }

    [Fact]
    public async Task REQ001_opens_a_session_without_remember_me_for_the_new_player_and_returns_the_opaque_token()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        var player = await StoredPlayer();
        var session = await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001"));
        Assert.NotNull(session);
        Assert.Equal(player.Id, session!.PlayerId);
        Assert.False(session.RememberMe);
        Assert.Equal(Now, session.CreatedAt);
        Assert.Equal(Now.AddMinutes(30), session.ExpiresAt);
        Assert.Equal("tok-0001", result.Value.SessionToken);
        Assert.Equal(session.ExpiresAt, result.Value.ExpiresAt);
    }

    [Fact]
    public async Task REQ001_persists_player_and_session_together_inside_a_unit_of_work_block()
    {
        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(_uow.TransactionOpenDuringLastCommit);
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task REQ001_persists_nothing_when_a_step_inside_the_unit_of_work_fails()
    {
        var failingTokens = new ThrowingTokenGenerator();

        await Assert.ThrowsAsync<InvalidOperationException>(() => NewUseCase(failingTokens).ExecuteAsync(ValidCommand()));

        Assert.Null(await _players.FindByEmail(AnEmail(RawEmail)));
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
    }

    [Fact]
    public async Task REQ001_REQ045_response_is_a_session_summary_with_the_player_data_and_no_email_or_hash()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        var summary = result.Value.Summary;
        Assert.Equal("Ana", summary.DisplayName);
        Assert.True(summary.HasPassword);
        Assert.False(summary.HasGoogle);
        Assert.Equal("preset", summary.Avatar.Kind);
        Assert.Equal(DefaultPresetId, summary.Avatar.PresetId);
        Assert.Equal("/avatars/preset-02.png", summary.Avatar.ImageUrl);
        Assert.Equal("Avatar: Planeswalker", summary.Avatar.Alt);
        var members = typeof(SessionSummary).GetProperties().Select(p => p.Name)
            .Concat(typeof(AuthenticatedSession).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain(members, name =>
            name.Contains("email", StringComparison.OrdinalIgnoreCase)
            || name.Contains("hash", StringComparison.OrdinalIgnoreCase)
            || name.Contains("password", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("HasPassword", StringComparison.Ordinal));
    }

    [Fact]
    public async Task REQ041_successful_registration_is_not_delayed_and_records_no_throttle_event()
    {
        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Empty(_delayer.Delays);
        Assert.Equal(0, await _attempts.CountByIpAsync(_keyedHasher.Hash(Ip), Now.AddHours(-1)));
    }

    private sealed class ThrowingTokenGenerator : ISecureTokenGenerator
    {
        public string Generate() => throw new InvalidOperationException("falha ao gerar token");
    }
}
