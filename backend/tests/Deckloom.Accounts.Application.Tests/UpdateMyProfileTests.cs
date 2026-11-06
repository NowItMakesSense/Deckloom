using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-046 (REQ-014, 015, 016, 037, 040, 046): edicao do perfil do Jogador autenticado. RED: os tipos abaixo ainda nao existem.
// Forma esperada (convencao do T-030, design.md linhas 360 e 412: a identidade vem sempre do AuthenticatedPlayer):
//   sealed record UpdateMyProfileCommand(AuthenticatedPlayer? Caller, string? DisplayName, string? Bio,
//       string? FavoriteTcg, IReadOnlyList<string>? FormatIds)       // campos crus de PUT /me/profile (design linha 1553)
//   sealed class UpdateMyProfile(IPlayerRepository players, IFormatCatalog formats,
//       IAvatarPresetCatalog avatarPresetCatalog, IUnitOfWork unitOfWork)
//     Task<Result<Profile>> ExecuteAsync(UpdateMyProfileCommand, CancellationToken ct = default)
// Saida: o mesmo Profile do T-045 (design linhas 1525-1530; PUT devolve "200 Profile", linha 1553), com preferredFormats so ativos.
// Erros (design linhas 1553 e 1594): Result.Failure com TODOS os erros, Field = nome do contrato da API, na ordem
//   displayName, bio, favoriteTcg, formatIds (via Player.UpdateProfile do T-016 / ValidationErrorCollector).
//   Sem Caller, ou Jogador removido -> DomainError("unauthenticated") sem campo (como GetMyProfile).
// Concorrencia otimista (design linhas 1382 e 1716; tasks.md linha 1576: "xmin em PostgreSQL, token de teste no SQLite"):
//   o adaptador EF traduz DbUpdateConcurrencyException para uma excecao do Domain, assim como faz com a unicidade
//   (UniqueConstraintViolationException, design linha 297). A spec NAO nomeia essa excecao; DECISAO deste teste:
//   `sealed class ConcurrencyConflictException : Exception` (construtor sem parametros) em Deckloom.Accounts.Domain,
//   ao lado de UniqueConstraintViolationException. O caso de uso a captura em unitOfWork.ExecuteAsync e devolve
//   DomainError("concurrency_conflict") sem campo (409), nunca excecao. O fake nao precisa mudar: o teste usa um
//   IUnitOfWork local que lanca a excecao no commit.
public class UpdateMyProfileTests
{
    private static readonly DomainError Unauthenticated = new("unauthenticated");
    private static readonly DomainError ConcurrencyConflict = new("concurrency_conflict");

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _avatars = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);
    private readonly InMemoryFormatCatalog _formats = new(
    [
        new FormatEntry(new FormatId("standard"), "Standard", 1, true),
        new FormatEntry(new FormatId("modern"), "Modern", 2, true),
        new FormatEntry(new FormatId("commander"), "Commander", 4, true),
    ]);

    public UpdateMyProfileTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
    }

    private UpdateMyProfile NewUseCase(IUnitOfWork? uow = null) => new(_players, _formats, _avatars, uow ?? _uow);

    private async Task<AuthenticatedPlayer> SignInAsync(Player player, string token = "raw-token")
    {
        await _players.Add(player);
        await _uow.CommitAsync();
        var session = Session.Start(player.Id, _tokenHasher.Hash(token), false, Now);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        var authenticate = new AuthenticateSession(_sessions, _players, _uow, _tokenHasher, _avatars, _clock);
        return (await authenticate.ExecuteAsync(new AuthenticateSessionCommand(token))).Value;
    }

    private List<FormatId> ActiveIds() =>
        _formats.ListActiveAsync().Result.Select(f => f.Id).ToList();

    private Player ACompletePlayer()
    {
        var player = APasswordPlayer();
        Assert.True(player.UpdateProfile(
            "Ana", "Jogo Pauper e Commander", "mtg",
            [new FormatId("modern"), new FormatId("commander")], ActiveIds()).IsSuccess);
        player.ClearDomainEvents();
        return player;
    }

    private Task<Result<Profile>> UpdateAsync(
        AuthenticatedPlayer? caller,
        string? displayName = "Ana Mage",
        string? bio = "Jogo Commander",
        string? favoriteTcg = "mtg",
        IReadOnlyList<string>? formatIds = null,
        IUnitOfWork? uow = null) =>
        NewUseCase(uow).ExecuteAsync(
            new UpdateMyProfileCommand(caller, displayName, bio, favoriteTcg, formatIds ?? ["standard"]));

    private async Task AssertPlayerUnchangedAsync(Player player)
    {
        var stored = await _players.GetById(player.Id);
        Assert.Equal("Ana", stored!.DisplayName.Value);
        Assert.Equal("Jogo Pauper e Commander", stored.Bio?.Value);
        Assert.Equal(FavoriteTcg.Default, stored.FavoriteTcg);
        Assert.Equal(["commander", "modern"], stored.Formats.Ids.Select(i => i.Value).Order(StringComparer.Ordinal));
        Assert.Empty(stored.DomainEvents);
    }

    [Fact]
    public async Task REQ014_a_valid_edit_persists_and_returns_the_updated_profile()
    {
        var player = APasswordPlayer();
        var caller = await SignInAsync(player);

        var result = await UpdateAsync(caller, "Ana Mage", "Jogo Commander", "mtg", ["standard", "commander"]);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ana Mage", result.Value.DisplayName);
        Assert.Equal("Jogo Commander", result.Value.Bio);
        Assert.Equal(new FavoriteTcgItem("mtg", "Magic: The Gathering"), result.Value.FavoriteTcg);
        Assert.Equal(
            [new FormatOption("standard", "Standard"), new FormatOption("commander", "Commander")],
            result.Value.PreferredFormats);
        Assert.True(result.Value.HasPassword);
        Assert.False(result.Value.HasGoogle);
        var stored = (await _players.GetById(player.Id))!;
        Assert.Equal("Ana Mage", stored.DisplayName.Value);
        Assert.Equal("Jogo Commander", stored.Bio?.Value);
        Assert.Equal(["standard", "commander"], stored.Formats.Ids.Select(i => i.Value));
    }

    [Fact]
    public async Task REQ014_the_returned_profile_keeps_the_players_own_avatar()
    {
        var caller = await SignInAsync(APasswordPlayer());

        var result = await UpdateAsync(caller);

        Assert.Equal(
            new AvatarView(AvatarView.PresetKind, "preset-01", "/avatars/preset-01.png", "Avatar: Dragao"),
            result.Value.Avatar);
    }

    [Fact]
    public async Task REQ015_an_empty_bio_and_no_formats_are_accepted_and_clear_the_values()
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);

        var result = await UpdateAsync(caller, "Ana", null, "mtg", []);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Bio);
        Assert.Empty(result.Value.PreferredFormats);
        Assert.Null((await _players.GetById(player.Id))!.Bio);
    }

    [Theory]
    [InlineData("An")]
    [InlineData("Abcdefghijklmnopqrstuvwxyz01234")]
    public async Task REQ014_a_name_with_2_or_31_characters_is_a_length_error_on_displayName(string name)
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);

        var result = await UpdateAsync(caller, name);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("length", "displayName"), Assert.Single(result.Errors));
        await AssertPlayerUnchangedAsync(player);
    }

    [Fact]
    public async Task REQ040_a_name_with_a_control_character_is_invalid_characters_on_displayName()
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);

        var result = await UpdateAsync(caller, "Ana\u0007Mage");

        Assert.Equal(new DomainError("invalid_characters", "displayName"), Assert.Single(result.Errors));
        await AssertPlayerUnchangedAsync(player);
    }

    [Fact]
    public async Task REQ015_a_bio_with_281_characters_is_too_long_on_bio()
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);

        var result = await UpdateAsync(caller, bio: new string('a', 281));

        Assert.Equal(new DomainError("too_long", "bio"), Assert.Single(result.Errors));
        await AssertPlayerUnchangedAsync(player);
    }

    [Fact]
    public async Task REQ040_a_bio_with_a_nul_character_is_invalid_characters_on_bio()
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);

        var result = await UpdateAsync(caller, bio: "oi\0tchau");

        Assert.Equal(new DomainError("invalid_characters", "bio"), Assert.Single(result.Errors));
        await AssertPlayerUnchangedAsync(player);
    }

    [Fact]
    public async Task REQ015_a_bio_with_exactly_280_characters_and_a_line_break_is_accepted()
    {
        var caller = await SignInAsync(APasswordPlayer());

        var result = await UpdateAsync(caller, bio: new string('a', 139) + "\n" + new string('b', 140));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task REQ016_a_favorite_tcg_other_than_magic_is_invalid_on_favoriteTcg()
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);

        var result = await UpdateAsync(caller, favoriteTcg: "Pokemon");

        Assert.Equal(new DomainError("invalid", "favoriteTcg"), Assert.Single(result.Errors));
        await AssertPlayerUnchangedAsync(player);
    }

    [Fact]
    public async Task REQ037_a_nonexistent_format_is_invalid_on_formatIds()
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);

        var result = await UpdateAsync(caller, formatIds: ["standard", "no-such-format"]);

        Assert.Equal(new DomainError("invalid", "formatIds"), Assert.Single(result.Errors));
        await AssertPlayerUnchangedAsync(player);
    }

    [Fact]
    public async Task REQ046_an_inactive_format_the_player_does_not_have_is_invalid_on_formatIds()
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);
        _formats.Deactivate(new FormatId("standard"));

        var result = await UpdateAsync(caller, formatIds: ["standard"]);

        Assert.Equal(new DomainError("invalid", "formatIds"), Assert.Single(result.Errors));
        await AssertPlayerUnchangedAsync(player);
    }

    [Fact]
    public async Task REQ037_all_field_errors_are_returned_together_in_the_contract_order()
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);

        var result = await UpdateAsync(caller, "An", new string('a', 281), "Pokemon", ["no-such-format"]);

        Assert.Equal(
            [
                new DomainError("length", "displayName"),
                new DomainError("too_long", "bio"),
                new DomainError("invalid", "favoriteTcg"),
                new DomainError("invalid", "formatIds"),
            ],
            result.Errors);
        await AssertPlayerUnchangedAsync(player);
    }

    [Fact]
    public async Task REQ046_an_inactive_format_is_preserved_when_the_client_does_not_send_it()
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);
        _formats.Deactivate(new FormatId("modern"));

        var result = await UpdateAsync(caller, formatIds: ["standard"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(["standard"], result.Value.PreferredFormats.Select(f => f.Id));
        _formats.Activate(new FormatId("modern"));
        var stored = (await _players.GetById(player.Id))!;
        Assert.Equal(
            ["modern", "standard"],
            stored.Formats.Ids.Select(i => i.Value).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task REQ011_without_a_session_the_update_is_unauthenticated()
    {
        var result = await UpdateAsync(null);

        Assert.True(result.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ011_a_player_removed_after_authentication_is_unauthenticated()
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);
        await _players.Remove(player);
        await _uow.CommitAsync();

        var result = await UpdateAsync(caller);

        Assert.True(result.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ011_the_update_acts_only_on_the_player_of_the_session()
    {
        var ana = ACompletePlayer();
        var gui = AGooglePlayer("gui@example.com", "sub-1");
        var anaCaller = await SignInAsync(ana, "ana-token");
        await SignInAsync(gui, "gui-token");

        var result = await UpdateAsync(anaCaller, "Ana Mage");

        Assert.Equal("Ana Mage", result.Value.DisplayName);
        Assert.Equal("Ana Mage", (await _players.GetById(ana.Id))!.DisplayName.Value);
        var other = (await _players.GetById(gui.Id))!;
        Assert.Equal("Gui", other.DisplayName.Value);
        Assert.Null(other.Bio);
        Assert.Empty(other.Formats.Ids);
    }

    [Fact]
    public async Task REQ014_a_display_name_already_used_by_another_player_is_accepted()
    {
        var ana = APasswordPlayer();
        var gui = AGooglePlayer("gui@example.com", "sub-1");
        await SignInAsync(ana, "ana-token");
        var guiCaller = await SignInAsync(gui, "gui-token");

        var result = await UpdateAsync(guiCaller, "Ana");

        Assert.True(result.IsSuccess);
        Assert.Equal("Ana", result.Value.DisplayName);
        Assert.Equal("Ana", (await _players.GetById(ana.Id))!.DisplayName.Value);
        Assert.Equal("Ana", (await _players.GetById(gui.Id))!.DisplayName.Value);
    }

    [Fact]
    public async Task REQ014_an_optimistic_concurrency_conflict_becomes_concurrency_conflict_and_not_an_exception()
    {
        var caller = await SignInAsync(ACompletePlayer());

        var result = await UpdateAsync(caller, uow: new ConflictingUnitOfWork(_uow));

        Assert.True(result.IsFailure);
        Assert.Equal(ConcurrencyConflict, Assert.Single(result.Errors));
    }

    // Simula o adaptador EF: o trabalho roda e o commit estoura com a traducao de DbUpdateConcurrencyException.
    private sealed class ConflictingUnitOfWork(InMemoryUnitOfWork inner) : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken ct = default) => throw new ConcurrencyConflictException();

        public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct = default) =>
            ExecuteAsync<object?>(
                async token =>
                {
                    await work(token);
                    return null;
                },
                ct);

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) =>
            inner.ExecuteAsync<T>(
                async token =>
                {
                    await work(token);
                    throw new ConcurrencyConflictException();
                },
                ct);
    }
}
