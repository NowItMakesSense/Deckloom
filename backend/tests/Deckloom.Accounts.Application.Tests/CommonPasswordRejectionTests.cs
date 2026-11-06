using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// SEC-05 (ASVS 2.1.7, REQ-038): senha comum e rejeitada so quando e DEFINIDA (cadastro e redefinicao);
// login nao aplica a regra (contas existentes continuam entrando).
public class CommonPasswordRejectionTests
{
    private const string Ip = "203.0.113.7";
    private const string TermsVersion = "2026-10-01";
    private const string RawToken = "reset-token-raw";
    private const string Listed = "password1";
    private const string ListedOtherCase = "PASSWORD1";
    private const string Unlisted = "zx9-plmq1";

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

    public CommonPasswordRejectionTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _attempts = new InMemoryAccessAttemptStore(_uow);
    }

    private AccessThrottle NewThrottle() =>
        new(_attempts, _keyedHasher, _delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock);

    private RegisterWithPassword NewRegister() =>
        new(_players, _sessions, _uow, _hasher, _tokens, _tokenHasher, _terms, _catalog, NewThrottle(), _clock);

    private ResetPassword NewReset() =>
        new(_players, _sessions, _uow, _hasher, _tokenHasher, _clock);

    private LoginWithPassword NewLogin() =>
        new(_players, _sessions, _uow, _hasher, _tokens, _tokenHasher, _catalog, NewThrottle(), _clock);

    private static RegisterWithPasswordCommand Register(string password) =>
        new("ana@example.com", password, "Ana", true, TermsVersion, true, Ip);

    private async Task<Player> SeedResetPlayerAsync()
    {
        var player = APasswordPlayer("ana@example.com");
        Assert.True(player.IssuePasswordReset(_tokenHasher.Hash(RawToken), Now).IsSuccess);
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    [Fact]
    public async Task Registration_rejects_a_listed_password_on_the_password_field_without_hashing_or_creating()
    {
        var result = await NewRegister().ExecuteAsync(Register(Listed));

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("too_common", "password"), Assert.Single(result.Errors));
        Assert.Empty(_hasher.HashCalls);
        Assert.Null(await _players.FindByEmail(AnEmail("ana@example.com")));
    }

    [Fact]
    public async Task Registration_error_does_not_contain_the_password()
    {
        var result = await NewRegister().ExecuteAsync(Register(Listed));

        var text = string.Join('|', result.Errors.Select(e => $"{e.Code}:{e.Field}"));
        Assert.DoesNotContain(Listed, text);
    }

    [Fact]
    public async Task Registration_accepts_an_unlisted_password_of_the_same_length()
    {
        var result = await NewRegister().ExecuteAsync(Register(Unlisted));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Registration_matching_is_case_sensitive()
    {
        var result = await NewRegister().ExecuteAsync(Register(ListedOtherCase));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Reset_rejects_a_listed_password_on_the_new_password_field_and_does_not_consume_the_token()
    {
        var player = await SeedResetPlayerAsync();
        var oldHash = player.PasswordHash!.ToStoredString();

        var result = await NewReset().ExecuteAsync(new ResetPasswordCommand(RawToken, Listed));

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("too_common", "newPassword"), Assert.Single(result.Errors));
        var stored = (await _players.FindByEmail(AnEmail("ana@example.com")))!;
        Assert.NotNull(stored.PasswordResetToken);
        Assert.Equal(oldHash, stored.PasswordHash!.ToStoredString());
        Assert.DoesNotContain(stored.DomainEvents, e => e is PasswordChanged);
    }

    [Fact]
    public async Task Reset_matching_is_case_sensitive()
    {
        await SeedResetPlayerAsync();

        var result = await NewReset().ExecuteAsync(new ResetPasswordCommand(RawToken, ListedOtherCase));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Reset_accepts_an_unlisted_password()
    {
        await SeedResetPlayerAsync();

        var result = await NewReset().ExecuteAsync(new ResetPasswordCommand(RawToken, Unlisted));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Login_with_a_listed_password_on_an_existing_account_is_not_rejected_by_this_rule()
    {
        var player = Player.RegisterWithPassword(
            AnEmail("ana@example.com"),
            await _hasher.HashAsync(Password.Create(Listed).Value),
            DisplayName.Create("Ana").Value,
            TermsVersion,
            TermsVersion,
            true,
            true,
            Now,
            "preset-01").Value;
        await _players.Add(player);
        await _uow.CommitAsync();

        var result = await NewLogin().ExecuteAsync(new LoginWithPasswordCommand("ana@example.com", Listed, false, Ip));

        Assert.True(result.IsSuccess);
    }
}
