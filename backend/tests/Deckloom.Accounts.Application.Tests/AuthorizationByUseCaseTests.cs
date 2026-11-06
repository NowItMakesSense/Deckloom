using System.Reflection;
using System.Text.RegularExpressions;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-052 (REQ-011, REQ-013): autorizacao por caso de uso (J10, design.md: a identidade vem sempre do AuthenticatedPlayer).
// Guarda de regressao: os tipos ja existem (T-045..T-049); este teste fixa o contrato em todos os casos de uso protegidos.
// Contrato citado (sessao ausente/expirada/revogada):
//   - A falha acontece na AUTENTICACAO: AuthenticateSession devolve DomainError("unauthenticated") sem campo (design linha 1428).
//   - A API repassa o Caller nulo ao caso de uso; cada caso de uso protegido recusa `Caller == null` com a mesma
//     DomainError("unauthenticated") (convencao T-045..T-049), sem tocar em dados.
//   - Caller montado antes de o Jogador ser removido (sessao antiga) tambem e `unauthenticated`.
// Reflexao: comando = 1o parametro de ExecuteAsync; 1o parametro do construtor do comando e `AuthenticatedPlayer? Caller`;
//   nenhum parametro/propriedade do comando carrega PlayerId/SessionId/identificador de dono vindo do cliente.
public class AuthorizationByUseCaseTests
{
    private const string AnaToken = "ana-token";
    private const string GuiToken = "gui-token";
    private const string AnaKey = "avatars/ana-1";
    private const string GuiKey = "avatars/gui-1";
    private const string Password = "correct horse battery";
    private const string Ip = "203.0.113.7";

    private static readonly byte[] AnaImage = [1, 1];
    private static readonly byte[] GuiImage = [9, 9];

    private static readonly DomainError Unauthenticated = new("unauthenticated");

    private static readonly Type[] ProtectedUseCases =
    [
        typeof(GetMyProfile),
        typeof(UpdateMyProfile),
        typeof(ChooseAvatarPreset),
        typeof(UploadCustomAvatar),
        typeof(GetMyCustomAvatar),
        typeof(DeleteMyAccount),
    ];

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly InMemoryAccessAttemptStore _attempts;
    private readonly InMemoryAvatarStorage _storage;
    private readonly RecordingPlayerDecksPurger _purger;
    private readonly FakeAvatarImageProcessor _processor = new();
    private readonly FakePasswordHasher _hasher = new(600_000);
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeKeyedHasher _keyedHasher = new();
    private readonly RecordingDelayer _delayer = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _avatars = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, true, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, false, true),
    ]);
    private readonly InMemoryFormatCatalog _formats = new(
    [
        new FormatEntry(new FormatId("standard"), "Standard", 1, true),
        new FormatEntry(new FormatId("modern"), "Modern", 2, true),
        new FormatEntry(new FormatId("commander"), "Commander", 4, true),
    ]);

    public AuthorizationByUseCaseTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _attempts = new InMemoryAccessAttemptStore(_uow);
        _storage = new InMemoryAvatarStorage(_uow);
        _purger = new RecordingPlayerDecksPurger(_uow);
        _processor.AcceptWith([5, 5, 5]);
    }

    // ---------- (a) reflexao ----------

    public static TheoryData<Type> ProtectedUseCaseTypes() => new(ProtectedUseCases);

    private static Type CommandTypeOf(Type useCase)
    {
        var execute = Assert.Single(useCase.GetMethods(BindingFlags.Public | BindingFlags.Instance), m => m.Name == "ExecuteAsync");
        var parameters = execute.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        return parameters[0].ParameterType;
    }

    [Theory]
    [MemberData(nameof(ProtectedUseCaseTypes))]
    public void REQ011_the_command_receives_the_caller_as_its_first_parameter(Type useCase)
    {
        var command = CommandTypeOf(useCase);

        var constructor = Assert.Single(command.GetConstructors());
        var first = constructor.GetParameters()[0];
        Assert.Equal("Caller", first.Name);
        Assert.Equal(typeof(AuthenticatedPlayer), first.ParameterType);
    }

    [Theory]
    [MemberData(nameof(ProtectedUseCaseTypes))]
    public void REQ011_REQ013_no_command_carries_a_target_player_or_session_identifier_from_the_client(Type useCase)
    {
        var command = CommandTypeOf(useCase);
        var ownerIdName = new Regex("(player|user|owner|account|session)(id)?$|^id$", RegexOptions.IgnoreCase);
        var forbiddenTypes = new[] { typeof(PlayerId), typeof(SessionId) };

        var constructor = Assert.Single(command.GetConstructors());
        var offending = constructor.GetParameters()
            .Where(p => p.Name != "Caller")
            .Where(p => forbiddenTypes.Contains(UnderlyingType(p.ParameterType)) || ownerIdName.IsMatch(p.Name!))
            .Select(p => p.Name)
            .Concat(command.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name != "Caller")
                .Where(p => forbiddenTypes.Contains(UnderlyingType(p.PropertyType)) || ownerIdName.IsMatch(p.Name))
                .Select(p => p.Name))
            .ToList();

        Assert.Empty(offending);
    }

    private static Type UnderlyingType(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    // ---------- fixtures ----------

    private AccessThrottle NewThrottle() =>
        new(_attempts, _keyedHasher, _delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock);

    private AuthenticateSession NewAuthenticate() =>
        new(_sessions, _players, _uow, _tokenHasher, _avatars, _clock);

    private async Task<Player> SeedPlayerAsync(string email, string name, string bio, string key, byte[] image, string formatId)
    {
        var hash = await _hasher.HashAsync(Deckloom.Accounts.Domain.Password.Create(Password).Value);
        var player = Player.RegisterWithPassword(
            AnEmail(email), hash, DisplayName.Create(name).Value, "2026-10-01", "2026-10-01", true, true, Now, "preset-01").Value;
        var active = (await _formats.ListActiveAsync()).Select(f => f.Id).ToList();
        Assert.True(player.UpdateProfile(name, bio, "mtg", [new FormatId(formatId)], active).IsSuccess);
        Assert.True(player.SetCustomAvatar(key).IsSuccess);
        _storage.SeedCommitted(key, image);
        player.ClearDomainEvents();
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    private async Task<(Player Ana, Player Gui)> SeedBothAsync()
    {
        var ana = await SeedPlayerAsync("ana@example.com", "Ana", "Bio da Ana", AnaKey, AnaImage, "modern");
        var gui = await SeedPlayerAsync("gui@example.com", "Gui", "Bio do Gui", GuiKey, GuiImage, "commander");
        await AddSessionAsync(ana, AnaToken);
        await AddSessionAsync(gui, GuiToken);
        return (ana, gui);
    }

    private async Task<Session> AddSessionAsync(Player player, string token)
    {
        var session = Session.Start(player.Id, _tokenHasher.Hash(token), false, Now);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        return session;
    }

    private async Task<AuthenticatedPlayer> CallerAsync(string token) =>
        (await NewAuthenticate().ExecuteAsync(new AuthenticateSessionCommand(token))).Value;

    private delegate Task<(bool Failed, IReadOnlyList<DomainError> Errors)> ProtectedCall(AuthenticatedPlayer? caller);

    private static async Task<(bool, IReadOnlyList<DomainError>)> Summarize<T>(Task<Result<T>> pending)
    {
        var result = await pending;
        return (result.IsFailure, result.IsFailure ? result.Errors.ToList() : []);
    }

    private Dictionary<string, ProtectedCall> AllProtectedCalls() => new Dictionary<string, ProtectedCall>
    {
        [nameof(GetMyProfile)] = caller => Summarize(
            new GetMyProfile(_players, _formats, _avatars).ExecuteAsync(new GetMyProfileCommand(caller))),
        [nameof(UpdateMyProfile)] = caller => Summarize(
            new UpdateMyProfile(_players, _formats, _avatars, _uow)
                .ExecuteAsync(new UpdateMyProfileCommand(caller, "Novo", "Nova bio", "mtg", ["standard"]))),
        [nameof(ChooseAvatarPreset)] = caller => Summarize(
            new ChooseAvatarPreset(_players, _avatars, _storage, _uow)
                .ExecuteAsync(new ChooseAvatarPresetCommand(caller, "preset-02"))),
        [nameof(UploadCustomAvatar)] = caller => Summarize(
            new UploadCustomAvatar(_players, _avatars, _processor, _storage, _uow)
                .ExecuteAsync(new UploadCustomAvatarCommand(caller, [1, 2, 3]))),
        [nameof(GetMyCustomAvatar)] = caller => Summarize(
            new GetMyCustomAvatar(_players, _storage).ExecuteAsync(new GetMyCustomAvatarCommand(caller))),
        [nameof(DeleteMyAccount)] = caller => Summarize(
            new DeleteMyAccount(_players, _sessions, _purger, _storage, _uow, _hasher, NewThrottle(), _clock)
                .ExecuteAsync(new DeleteMyAccountCommand(caller, Password, true, Ip))),
    };

    // ---------- (b) sessao ausente / expirada / revogada ----------

    [Theory]
    [InlineData("absent")]
    [InlineData("unknown")]
    [InlineData("expired")]
    [InlineData("revoked")]
    public async Task REQ011_every_protected_use_case_is_unauthenticated_without_a_valid_session(string scenario)
    {
        var (ana, gui) = await SeedBothAsync();
        var sessionToken = scenario switch
        {
            "absent" => null,
            "unknown" => "some-other-token",
            _ => AnaToken,
        };
        if (scenario == "expired")
        {
            _clock.Advance(TimeSpan.FromMinutes(31));
        }

        if (scenario == "revoked")
        {
            var session = await _sessions.FindByTokenHash(_tokenHasher.Hash(AnaToken));
            session!.Revoke(SessionRevocationReason.Logout, Now);
        }

        var authentication = await NewAuthenticate().ExecuteAsync(new AuthenticateSessionCommand(sessionToken));

        Assert.True(authentication.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(authentication.Errors));
        AuthenticatedPlayer? caller = authentication.IsSuccess ? authentication.Value : null;
        foreach (var (name, call) in AllProtectedCalls())
        {
            var (failed, errors) = await call(caller);

            Assert.True(failed, name);
            Assert.Equal(Unauthenticated, Assert.Single(errors));
        }

        await AssertNothingChangedAsync(ana, gui);
    }

    [Fact]
    public async Task REQ011_every_protected_use_case_is_unauthenticated_for_a_caller_whose_player_no_longer_exists()
    {
        var (ana, gui) = await SeedBothAsync();
        var staleCaller = await CallerAsync(AnaToken);
        await _players.Remove(ana);
        await _uow.CommitAsync();

        foreach (var (name, call) in AllProtectedCalls())
        {
            var (failed, errors) = await call(staleCaller);

            Assert.True(failed, name);
            Assert.Equal(Unauthenticated, Assert.Single(errors));
        }

        Assert.Empty(_purger.Purged);
        await AssertGuiUntouchedAsync(gui);
    }

    private async Task AssertNothingChangedAsync(Player ana, Player gui)
    {
        Assert.Equal("Ana", (await _players.GetById(ana.Id))!.DisplayName.Value);
        Assert.Equal(AnaKey, Assert.IsType<CustomAvatar>((await _players.GetById(ana.Id))!.Avatar).StorageKey);
        Assert.Equal(AnaImage, await _storage.ReadAsync(AnaKey));
        Assert.Empty(_purger.Purged);
        await AssertGuiUntouchedAsync(gui);
    }

    private async Task AssertGuiUntouchedAsync(Player gui)
    {
        var stored = (await _players.GetById(gui.Id))!;
        Assert.Equal("Gui", stored.DisplayName.Value);
        Assert.Equal("Bio do Gui", stored.Bio!.Value);
        Assert.Equal(["commander"], stored.Formats.Ids.Select(i => i.Value));
        Assert.Equal(GuiKey, Assert.IsType<CustomAvatar>(stored.Avatar).StorageKey);
        Assert.Equal(GuiImage, await _storage.ReadAsync(GuiKey));
    }

    // ---------- (c) Jogador A nao le nem altera dados de B ----------

    [Fact]
    public async Task REQ013_GetMyProfile_reads_only_the_caller_profile()
    {
        var (_, _) = await SeedBothAsync();
        var anaCaller = await CallerAsync(AnaToken);

        var result = await new GetMyProfile(_players, _formats, _avatars).ExecuteAsync(new GetMyProfileCommand(anaCaller));

        Assert.True(result.IsSuccess);
        Assert.Equal("Ana", result.Value.DisplayName);
        Assert.Equal("Bio da Ana", result.Value.Bio);
        Assert.Equal(["modern"], result.Value.PreferredFormats.Select(f => f.Id));
    }

    [Fact]
    public async Task REQ013_UpdateMyProfile_changes_only_the_caller()
    {
        var (ana, gui) = await SeedBothAsync();
        var anaCaller = await CallerAsync(AnaToken);

        var result = await new UpdateMyProfile(_players, _formats, _avatars, _uow)
            .ExecuteAsync(new UpdateMyProfileCommand(anaCaller, "Ana Mage", "Nova bio", "mtg", ["standard"]));

        Assert.True(result.IsSuccess);
        var storedAna = (await _players.GetById(ana.Id))!;
        Assert.Equal("Ana Mage", storedAna.DisplayName.Value);
        Assert.Equal(["standard"], storedAna.Formats.Ids.Select(i => i.Value));
        await AssertGuiUntouchedAsync(gui);
    }

    [Fact]
    public async Task REQ013_ChooseAvatarPreset_changes_only_the_caller_and_removes_only_the_callers_previous_image()
    {
        var (ana, gui) = await SeedBothAsync();
        var anaCaller = await CallerAsync(AnaToken);

        var result = await new ChooseAvatarPreset(_players, _avatars, _storage, _uow)
            .ExecuteAsync(new ChooseAvatarPresetCommand(anaCaller, "preset-02"));

        Assert.True(result.IsSuccess);
        Assert.Equal("preset-02", Assert.IsType<PresetAvatar>((await _players.GetById(ana.Id))!.Avatar).PresetId);
        Assert.Null(await _storage.ReadAsync(AnaKey));
        await AssertGuiUntouchedAsync(gui);
    }

    [Fact]
    public async Task REQ013_UploadCustomAvatar_changes_only_the_caller_and_removes_only_the_callers_previous_image()
    {
        var (ana, gui) = await SeedBothAsync();
        var anaCaller = await CallerAsync(AnaToken);

        var result = await new UploadCustomAvatar(_players, _avatars, _processor, _storage, _uow)
            .ExecuteAsync(new UploadCustomAvatarCommand(anaCaller, [1, 2, 3]));

        Assert.True(result.IsSuccess);
        var newKey = Assert.IsType<CustomAvatar>((await _players.GetById(ana.Id))!.Avatar).StorageKey;
        Assert.NotEqual(AnaKey, newKey);
        Assert.Equal<byte[]>([5, 5, 5], (await _storage.ReadAsync(newKey))!);
        Assert.Null(await _storage.ReadAsync(AnaKey));
        await AssertGuiUntouchedAsync(gui);
    }

    [Fact]
    public async Task REQ013_GetMyCustomAvatar_returns_only_the_callers_image_and_never_falls_back_to_another_player()
    {
        var (_, _) = await SeedBothAsync();
        var anaCaller = await CallerAsync(AnaToken);
        var guiCaller = await CallerAsync(GuiToken);
        var useCase = new GetMyCustomAvatar(_players, _storage);

        var ana = await useCase.ExecuteAsync(new GetMyCustomAvatarCommand(anaCaller));
        var gui = await useCase.ExecuteAsync(new GetMyCustomAvatarCommand(guiCaller));
        await new ChooseAvatarPreset(_players, _avatars, _storage, _uow)
            .ExecuteAsync(new ChooseAvatarPresetCommand(anaCaller, "preset-02"));
        var anaAfterPreset = await useCase.ExecuteAsync(new GetMyCustomAvatarCommand(anaCaller));

        Assert.Equal(AnaImage, ana.Value);
        Assert.Equal(GuiImage, gui.Value);
        Assert.True(anaAfterPreset.IsFailure);
        Assert.Equal(new DomainError("not_found"), Assert.Single(anaAfterPreset.Errors));
    }

    [Fact]
    public async Task REQ013_DeleteMyAccount_removes_only_the_caller_and_leaves_the_other_player_and_session_intact()
    {
        var (ana, gui) = await SeedBothAsync();
        var anaCaller = await CallerAsync(AnaToken);

        var result = await new DeleteMyAccount(_players, _sessions, _purger, _storage, _uow, _hasher, NewThrottle(), _clock)
            .ExecuteAsync(new DeleteMyAccountCommand(anaCaller, Password, true, Ip));

        Assert.True(result.IsSuccess);
        Assert.Null(await _players.GetById(ana.Id));
        Assert.Null(await _storage.ReadAsync(AnaKey));
        Assert.Equal([ana.Id], _purger.Purged);
        await AssertGuiUntouchedAsync(gui);
        var guiSession = await NewAuthenticate().ExecuteAsync(new AuthenticateSessionCommand(GuiToken));
        Assert.True(guiSession.IsSuccess);
        Assert.Equal(gui.Id, guiSession.Value.PlayerId);
        Assert.True((await NewAuthenticate().ExecuteAsync(new AuthenticateSessionCommand(AnaToken))).IsFailure);
    }
}
