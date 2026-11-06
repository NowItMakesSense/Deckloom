using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-048 (REQ-019, REQ-020, REQ-021, REQ-049): o Jogador autenticado envia um avatar customizado. RED: os tipos abaixo ainda nao existem.
// Forma esperada (convencao do T-047):
//   sealed record UploadCustomAvatarCommand(AuthenticatedPlayer? Caller, byte[]? Image)   // bytes crus do campo `file`
//   sealed class UploadCustomAvatar(IPlayerRepository players, IAvatarPresetCatalog avatarPresetCatalog,
//       IAvatarImageProcessor imageProcessor, IAvatarStorage avatarStorage, IUnitOfWork unitOfWork)
//     Task<Result<AvatarView>> ExecuteAsync(UploadCustomAvatarCommand, CancellationToken ct = default)
// Saida: `200 {avatar: Avatar}` (design linha 1567) -> AvatarView kind "custom" (imageUrl "/api/v1/me/avatar/custom?v={key}",
//   alt AvatarViewBuilder.CustomAlt).
// Chave de armazenamento: a spec NAO fixa o formato (design linhas 1379/1535 so exigem que a URL mude a cada troca, "?v=<storage_key>").
//   O teste exige apenas: chave nao vazia, a mesma em Player.Avatar, no armazenamento e na URL, e DIFERENTE da chave anterior.
// Rejeicao (design linhas 1567 e 1586-1590; tabela do design): DomainError("avatar_rejected", Field = "size"|"type"|"content"),
//   o Field carrega o `reason` que a API traduz em 413/415/422 (a mensagem ao usuario e a mesma). Sem Caller/Jogador removido
//   -> DomainError("unauthenticated"). Excecao do processador -> reason "content" (design linha 1594: "excecao do codec vira 422 content").
// Ordem (design 1584-1593): o processamento acontece antes de tocar no Jogador/armazenamento; so o aceito grava, tudo numa
//   unidade de trabalho (Save do Jogador + SaveAsync da nova imagem + RemoveAsync da anterior). Excecao do bloco propaga.
public class UploadCustomAvatarTests
{
    private const string OldKey = "avatars/ana-old";

    private static readonly byte[] Original = [10, 20, 30];
    private static readonly byte[] Processed = [7, 7, 7, 7];

    private static readonly DomainError Unauthenticated = new("unauthenticated");

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly InMemoryAvatarStorage _storage;
    private readonly FakeAvatarImageProcessor _processor = new();
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _avatars = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, true, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, false, true),
    ]);

    public UploadCustomAvatarTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _storage = new InMemoryAvatarStorage(_uow);
        _processor.AcceptWith(Processed);
    }

    private UploadCustomAvatar NewUseCase(IUnitOfWork? uow = null) =>
        new(_players, _avatars, _processor, _storage, uow ?? _uow);

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

    private async Task<Player> ACustomPlayerAsync(string email = "ana@example.com", string key = OldKey)
    {
        var player = APasswordPlayer(email);
        Assert.True(player.SetCustomAvatar(key).IsSuccess);
        player.ClearDomainEvents();
        _storage.SeedCommitted(key, [1, 2, 3]);
        await _uow.CommitAsync();
        return player;
    }

    private Task<Result<AvatarView>> UploadAsync(AuthenticatedPlayer? caller, byte[]? image = null, IUnitOfWork? uow = null) =>
        NewUseCase(uow).ExecuteAsync(new UploadCustomAvatarCommand(caller, image ?? Original));

    [Fact]
    public async Task REQ019_an_accepted_image_becomes_the_custom_avatar_and_the_processed_bytes_are_stored()
    {
        var player = APasswordPlayer();
        var caller = await SignInAsync(player);

        var result = await UploadAsync(caller);

        Assert.True(result.IsSuccess);
        var stored = (await _players.GetById(player.Id))!;
        var custom = Assert.IsType<CustomAvatar>(stored.Avatar);
        Assert.False(string.IsNullOrWhiteSpace(custom.StorageKey));
        Assert.Equal(Processed, await _storage.ReadAsync(custom.StorageKey));
        Assert.Equal(
            new AvatarView(
                AvatarView.CustomKind,
                null,
                $"/api/v1/me/avatar/custom?v={Uri.EscapeDataString(custom.StorageKey)}",
                "Seu avatar personalizado"),
            result.Value);
    }

    [Fact]
    public async Task REQ019_the_processor_receives_the_original_bytes_and_the_original_is_never_stored()
    {
        var player = APasswordPlayer();
        var caller = await SignInAsync(player);

        await UploadAsync(caller);

        Assert.Equal(Original, Assert.Single(_processor.Received));
        var custom = Assert.IsType<CustomAvatar>((await _players.GetById(player.Id))!.Avatar);
        Assert.NotEqual(Original, await _storage.ReadAsync(custom.StorageKey));
    }

    [Fact]
    public async Task REQ019_the_first_custom_upload_emits_AvatarChanged_without_a_previous_key()
    {
        var player = APasswordPlayer();
        player.ClearDomainEvents();
        var caller = await SignInAsync(player);

        await UploadAsync(caller);

        var changed = Assert.IsType<AvatarChanged>(Assert.Single((await _players.GetById(player.Id))!.DomainEvents));
        Assert.Equal(player.Id, changed.PlayerId);
        Assert.Null(changed.PreviousStorageKey);
    }

    [Fact]
    public async Task REQ021_replacing_a_custom_avatar_stores_a_new_key_and_removes_the_previous_image()
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);

        var result = await UploadAsync(caller);

        Assert.True(result.IsSuccess);
        var custom = Assert.IsType<CustomAvatar>((await _players.GetById(player.Id))!.Avatar);
        Assert.NotEqual(OldKey, custom.StorageKey);
        Assert.Equal(Processed, await _storage.ReadAsync(custom.StorageKey));
        Assert.Null(await _storage.ReadAsync(OldKey));
    }

    [Fact]
    public async Task REQ021_replacing_a_custom_avatar_emits_AvatarChanged_with_the_previous_key()
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);

        await UploadAsync(caller);

        var changed = Assert.IsType<AvatarChanged>(Assert.Single((await _players.GetById(player.Id))!.DomainEvents));
        Assert.Equal(OldKey, changed.PreviousStorageKey);
    }

    [Fact]
    public async Task REQ021_the_whole_change_is_one_unit_of_work_so_a_rollback_keeps_the_old_image_and_stores_nothing_new()
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UploadAsync(caller, uow: new FailingUnitOfWork(_uow)));

        Assert.Equal(new byte[] { 1, 2, 3 }, await _storage.ReadAsync(OldKey));
        Assert.Equal(CustomAvatar.Create(OldKey).Value, (await _players.GetById(player.Id))!.Avatar);
    }

    [Theory]
    [InlineData(AvatarRejectionReason.Size, "size")]
    [InlineData(AvatarRejectionReason.Type, "type")]
    [InlineData(AvatarRejectionReason.Content, "content")]
    public async Task REQ020_a_rejected_image_fails_with_avatar_rejected_carrying_the_reason_and_keeps_the_current_avatar(
        AvatarRejectionReason reason,
        string expectedField)
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);
        _processor.RejectWith(reason);

        var result = await UploadAsync(caller);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("avatar_rejected", expectedField), Assert.Single(result.Errors));
        await AssertCustomAvatarUntouchedAsync(player);
    }

    // REQ-049: animada e dimensoes acima do limite chegam do processador como Content (design 1589) e seguem o mesmo caminho.
    [Fact]
    public async Task REQ049_an_animated_or_oversized_image_rejected_as_content_keeps_the_current_avatar_and_the_old_image()
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);
        _processor.RejectWith(AvatarRejectionReason.Content);

        var result = await UploadAsync(caller);

        Assert.Equal(new DomainError("avatar_rejected", "content"), Assert.Single(result.Errors));
        await AssertCustomAvatarUntouchedAsync(player);
    }

    [Fact]
    public async Task REQ020_a_rejection_on_a_preset_player_keeps_the_preset_and_stores_nothing()
    {
        var player = APasswordPlayer();
        player.ClearDomainEvents();
        var caller = await SignInAsync(player);
        _processor.RejectWith(AvatarRejectionReason.Type);

        var result = await UploadAsync(caller);

        Assert.True(result.IsFailure);
        var stored = (await _players.GetById(player.Id))!;
        Assert.Equal(PresetAvatar.Create("preset-01").Value, stored.Avatar);
        Assert.Empty(stored.DomainEvents);
    }

    [Fact]
    public async Task REQ020_a_processor_failure_becomes_a_content_rejection_without_changing_the_avatar()
    {
        var player = await ACustomPlayerAsync();
        var caller = await SignInAsync(player);

        var result = await NewUseCase(processor: new ThrowingProcessor())
            .ExecuteAsync(new UploadCustomAvatarCommand(caller, Original));

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("avatar_rejected", "content"), Assert.Single(result.Errors));
        await AssertCustomAvatarUntouchedAsync(player);
    }

    [Fact]
    public async Task REQ019_the_upload_acts_only_on_the_player_of_the_session()
    {
        var ana = APasswordPlayer();
        var gui = await ACustomPlayerAsync("gui@example.com", "avatars/gui-1");
        var anaCaller = await SignInAsync(ana, "ana-token");
        await SignInAsync(gui, "gui-token");

        await UploadAsync(anaCaller);

        Assert.Equal(CustomAvatar.Create("avatars/gui-1").Value, (await _players.GetById(gui.Id))!.Avatar);
        Assert.Equal(new byte[] { 1, 2, 3 }, await _storage.ReadAsync("avatars/gui-1"));
        Assert.IsType<CustomAvatar>((await _players.GetById(ana.Id))!.Avatar);
    }

    [Fact]
    public async Task REQ011_without_a_session_the_upload_is_unauthenticated_and_the_processor_is_not_called()
    {
        var result = await UploadAsync(null);

        Assert.True(result.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
        Assert.Empty(_processor.Received);
    }

    [Fact]
    public async Task REQ011_a_player_removed_after_authentication_is_unauthenticated()
    {
        var player = APasswordPlayer();
        var caller = await SignInAsync(player);
        await _players.Remove(player);
        await _uow.CommitAsync();

        var result = await UploadAsync(caller);

        Assert.True(result.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
    }

    private UploadCustomAvatar NewUseCase(IAvatarImageProcessor processor) =>
        new(_players, _avatars, processor, _storage, _uow);

    private async Task AssertCustomAvatarUntouchedAsync(Player player)
    {
        var stored = (await _players.GetById(player.Id))!;
        Assert.Equal(CustomAvatar.Create(OldKey).Value, stored.Avatar);
        Assert.Empty(stored.DomainEvents);
        Assert.Equal(new byte[] { 1, 2, 3 }, await _storage.ReadAsync(OldKey));
    }

    private sealed class ThrowingProcessor : IAvatarImageProcessor
    {
        public Task<AvatarProcessingResult> ProcessAsync(byte[] original, CancellationToken ct = default) =>
            throw new InvalidOperationException("native codec crashed");
    }

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
