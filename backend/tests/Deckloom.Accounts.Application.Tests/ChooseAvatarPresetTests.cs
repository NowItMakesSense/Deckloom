using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-047 (REQ-018, REQ-021): o Jogador autenticado escolhe um avatar da galeria de presets. RED: os tipos abaixo ainda nao existem.
// Forma esperada (convencao do T-030/T-046, design linhas 360 e 412: a identidade vem sempre do AuthenticatedPlayer):
//   sealed record ChooseAvatarPresetCommand(AuthenticatedPlayer? Caller, string? PresetId)   // corpo cru de PUT /me/avatar/preset
//   sealed class ChooseAvatarPreset(IPlayerRepository players, IAvatarPresetCatalog avatarPresetCatalog,
//       IAvatarStorage avatarStorage, IUnitOfWork unitOfWork)
//     Task<Result<AvatarView>> ExecuteAsync(ChooseAvatarPresetCommand, CancellationToken ct = default)
// Saida: `200 {avatar: Avatar}` (design linha 1566; tipo compartilhado Avatar, linhas 1533-1535) -> o AvatarView do T-045
//   (kind "preset", presetId, imageUrl do catalogo, alt "Avatar: {label}"), nao o Profile inteiro.
// Erro (design linha 1566): preset inexistente, inativo ou em branco -> DomainError("invalid", "presetId") (campo do contrato da
//   API; o agregado devolve Field "avatar" para branco, design linha 203, e o caso de uso o traduz). A validacao contra a
//   galeria ativa e da Application (design linha 204, IAvatarPresetCatalog.ContainsActiveAsync), antes de tocar no agregado.
//   Sem Caller, ou Jogador removido -> DomainError("unauthenticated") sem campo (como GetMyProfile/UpdateMyProfile).
// REQ-021 (design linhas 279 e 205): a troca real emite AvatarChanged(PlayerId, PreviousStorageKey?) e o caso de uso remove
//   a imagem anterior (a chave do evento) via IAvatarStorage.RemoveAsync DENTRO de unitOfWork.ExecuteAsync, junto com o Save.
//   Escolher o preset ja definido e sucesso sem evento e sem remocao (design linha 205). DECISAO deste teste: o caso de uso
//   NAO consome (PullDomainEvents) os eventos do agregado; o evento fica observavel em Player.DomainEvents apos o Save, como
//   ProfileUpdated nos testes do T-046. Excecao do bloco (rollback) propaga: o caso de uso nao a captura.
public class ChooseAvatarPresetTests
{
    private const string CustomKey = "avatars/ana-1";

    private static readonly DomainError Unauthenticated = new("unauthenticated");
    private static readonly DomainError InvalidPreset = new("invalid", "presetId");

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly InMemoryAvatarStorage _storage;
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _avatars = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
        new AvatarPresetEntry("mago-azul", "Mago Azul", "/avatars/mago-azul.png", 3, false, true),
        new AvatarPresetEntry("mago-aposentado", "Mago Aposentado", "/avatars/mago-aposentado.png", 4, false, false),
    ]);

    public ChooseAvatarPresetTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _storage = new InMemoryAvatarStorage(_uow);
    }

    private ChooseAvatarPreset NewUseCase(IUnitOfWork? uow = null) => new(_players, _avatars, _storage, uow ?? _uow);

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

    // Jogador com avatar customizado cuja imagem ja esta armazenada (confirmada).
    private async Task<Player> ACustomPlayerAsync(string email = "ana@example.com", string key = CustomKey)
    {
        var player = APasswordPlayer(email);
        Assert.True(player.SetCustomAvatar(key).IsSuccess);
        player.ClearDomainEvents();
        _storage.SeedCommitted(key, [1, 2, 3]);
        await _uow.CommitAsync();
        return player;
    }

    private Task<Result<AvatarView>> ChooseAsync(
        AuthenticatedPlayer? caller, string? presetId, IUnitOfWork? uow = null) =>
        NewUseCase(uow).ExecuteAsync(new ChooseAvatarPresetCommand(caller, presetId));

    [Fact]
    public async Task REQ018_an_active_preset_becomes_the_players_avatar_and_is_returned_as_the_avatar_view()
    {
        var player = APasswordPlayer();
        var caller = await SignInAsync(player);

        var result = await ChooseAsync(caller, "mago-azul");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new AvatarView(AvatarView.PresetKind, "mago-azul", "/avatars/mago-azul.png", "Avatar: Mago Azul"),
            result.Value);
        var stored = (await _players.GetById(player.Id))!;
        Assert.Equal(PresetAvatar.Create("mago-azul").Value, stored.Avatar);
    }

    [Fact]
    public async Task REQ018_the_default_preset_is_also_a_valid_choice()
    {
        var player = APasswordPlayer();
        var caller = await SignInAsync(player);

        var result = await ChooseAsync(caller, "preset-02");

        Assert.True(result.IsSuccess);
        Assert.Equal("preset-02", result.Value.PresetId);
        Assert.Equal(PresetAvatar.Create("preset-02").Value, (await _players.GetById(player.Id))!.Avatar);
    }

    [Fact]
    public async Task REQ018_choosing_the_current_preset_is_a_success_without_event()
    {
        var player = APasswordPlayer();
        player.ClearDomainEvents();
        var caller = await SignInAsync(player);

        var result = await ChooseAsync(caller, "preset-01");

        Assert.True(result.IsSuccess);
        Assert.Equal("preset-01", result.Value.PresetId);
        Assert.Empty((await _players.GetById(player.Id))!.DomainEvents);
    }

    [Fact]
    public async Task REQ018_a_preset_change_emits_AvatarChanged_without_a_previous_key()
    {
        var player = APasswordPlayer();
        player.ClearDomainEvents();
        var caller = await SignInAsync(player);

        await ChooseAsync(caller, "mago-azul");

        var changed = Assert.IsType<AvatarChanged>(Assert.Single((await _players.GetById(player.Id))!.DomainEvents));
        Assert.Equal(player.Id, changed.PlayerId);
        Assert.Null(changed.PreviousStorageKey);
    }

    [Fact]
    public async Task REQ018_a_nonexistent_preset_is_invalid_on_presetId_and_changes_nothing()
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);

        var result = await ChooseAsync(caller, "no-such-preset");

        Assert.True(result.IsFailure);
        Assert.Equal(InvalidPreset, Assert.Single(result.Errors));
        await AssertCustomAvatarUntouchedAsync(player);
    }

    [Fact]
    public async Task REQ018_an_inactive_preset_is_invalid_on_presetId_and_changes_nothing()
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);

        var result = await ChooseAsync(caller, "mago-aposentado");

        Assert.True(result.IsFailure);
        Assert.Equal(InvalidPreset, Assert.Single(result.Errors));
        await AssertCustomAvatarUntouchedAsync(player);
    }

    [Fact]
    public async Task REQ018_a_preset_deactivated_after_the_catalog_was_loaded_is_invalid()
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);
        _avatars.Deactivate("mago-azul");

        var result = await ChooseAsync(caller, "mago-azul");

        Assert.Equal(InvalidPreset, Assert.Single(result.Errors));
        await AssertCustomAvatarUntouchedAsync(player);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task REQ018_a_blank_preset_is_invalid_on_presetId_and_changes_nothing(string? presetId)
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);

        var result = await ChooseAsync(caller, presetId);

        Assert.True(result.IsFailure);
        Assert.Equal(InvalidPreset, Assert.Single(result.Errors));
        await AssertCustomAvatarUntouchedAsync(player);
    }

    [Fact]
    public async Task REQ021_swapping_a_custom_avatar_for_a_preset_removes_the_custom_image_from_storage()
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);

        var result = await ChooseAsync(caller, "mago-azul");

        Assert.True(result.IsSuccess);
        Assert.Null(await _storage.ReadAsync(CustomKey));
        Assert.Equal(PresetAvatar.Create("mago-azul").Value, (await _players.GetById(player.Id))!.Avatar);
    }

    [Fact]
    public async Task REQ021_swapping_a_custom_avatar_for_a_preset_emits_AvatarChanged_with_the_previous_key()
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);

        await ChooseAsync(caller, "mago-azul");

        var changed = Assert.IsType<AvatarChanged>(Assert.Single((await _players.GetById(player.Id))!.DomainEvents));
        Assert.Equal(player.Id, changed.PlayerId);
        Assert.Equal(CustomKey, changed.PreviousStorageKey);
    }

    [Fact]
    public async Task REQ021_the_removal_is_in_the_same_unit_of_work_so_a_rollback_keeps_the_image()
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ChooseAsync(caller, "mago-azul", new FailingUnitOfWork(_uow)));

        Assert.Equal(new byte[] { 1, 2, 3 }, await _storage.ReadAsync(CustomKey));
    }

    [Fact]
    public async Task REQ021_choosing_a_preset_while_already_on_a_preset_does_not_touch_any_stored_image()
    {
        var other = await ACustomPlayerAsync("gui@example.com", "avatars/gui-1");
        await SignInAsync(other, "gui-token");
        var caller = await SignInAsync(APasswordPlayer(), "ana-token");

        await ChooseAsync(caller, "mago-azul");

        Assert.Equal(new byte[] { 1, 2, 3 }, await _storage.ReadAsync("avatars/gui-1"));
    }

    [Fact]
    public async Task REQ018_the_choice_acts_only_on_the_player_of_the_session()
    {
        var ana = APasswordPlayer();
        var gui = await ACustomPlayerAsync("gui@example.com", "avatars/gui-1");
        var anaCaller = await SignInAsync(ana, "ana-token");
        await SignInAsync(gui, "gui-token");

        await ChooseAsync(anaCaller, "mago-azul");

        Assert.Equal(CustomAvatar.Create("avatars/gui-1").Value, (await _players.GetById(gui.Id))!.Avatar);
        Assert.Equal(PresetAvatar.Create("mago-azul").Value, (await _players.GetById(ana.Id))!.Avatar);
    }

    [Fact]
    public async Task REQ011_without_a_session_the_choice_is_unauthenticated()
    {
        var result = await ChooseAsync(null, "mago-azul");

        Assert.True(result.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ011_a_player_removed_after_authentication_is_unauthenticated()
    {
        var player = APasswordPlayer();
        var caller = await SignInAsync(player);
        await _players.Remove(player);
        await _uow.CommitAsync();

        var result = await ChooseAsync(caller, "mago-azul");

        Assert.True(result.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
    }

    private async Task AssertCustomAvatarUntouchedAsync(Player player)
    {
        var stored = (await _players.GetById(player.Id))!;
        Assert.Equal(CustomAvatar.Create(CustomKey).Value, stored.Avatar);
        Assert.Empty(stored.DomainEvents);
        Assert.Equal(new byte[] { 1, 2, 3 }, await _storage.ReadAsync(CustomKey));
    }

    // Simula uma falha depois do trabalho (por exemplo, o commit do adaptador EF): o bloco roda e a unidade de trabalho
    // do fake descarta as mudancas pendentes (Save/Remove do armazenamento) antes de propagar a excecao.
    private sealed class FailingUnitOfWork(InMemoryUnitOfWork inner) : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken ct = default) => throw new InvalidOperationException("commit failed");

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
                    throw new InvalidOperationException("commit failed");
                },
                ct);
    }
}
