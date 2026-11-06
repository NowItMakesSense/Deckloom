using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-048 (REQ-019): GET /me/avatar/custom devolve a imagem do avatar customizado do dono da sessao. RED: tipos ainda inexistentes.
// Forma esperada:
//   sealed record GetMyCustomAvatarCommand(AuthenticatedPlayer? Caller)
//   sealed class GetMyCustomAvatar(IPlayerRepository players, IAvatarStorage avatarStorage)
//     Task<Result<byte[]>> ExecuteAsync(GetMyCustomAvatarCommand, CancellationToken ct = default)
// A imagem vem SEMPRE da chave do avatar do proprio Jogador da sessao; o parametro `?v=` do design (linha 1568) so versiona
// o cache e nao entra no comando (nenhuma chave vinda do cliente, logo nao ha leitura da imagem de outro Jogador).
// Erros: sem Caller/Jogador removido -> DomainError("unauthenticated") (401); avatar preset, ou imagem ausente do
// armazenamento -> DomainError("not_found") (404; a spec fixa so o status 404, design linha 1568 - o codigo "not_found" e decisao
// deste teste).
public class GetMyCustomAvatarTests
{
    private static readonly DomainError Unauthenticated = new("unauthenticated");
    private static readonly DomainError NotFound = new("not_found");

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly InMemoryAvatarStorage _storage;
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _avatars = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, true, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, false, true),
    ]);

    public GetMyCustomAvatarTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _storage = new InMemoryAvatarStorage(_uow);
    }

    private GetMyCustomAvatar NewUseCase() => new(_players, _storage);

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

    private async Task<Player> ACustomPlayerAsync(string email, string key, byte[]? image)
    {
        var player = APasswordPlayer(email);
        Assert.True(player.SetCustomAvatar(key).IsSuccess);
        player.ClearDomainEvents();
        if (image is not null)
        {
            _storage.SeedCommitted(key, image);
        }

        await _uow.CommitAsync();
        return player;
    }

    private Task<Result<byte[]>> GetAsync(AuthenticatedPlayer? caller) =>
        NewUseCase().ExecuteAsync(new GetMyCustomAvatarCommand(caller));

    [Fact]
    public async Task REQ019_returns_the_stored_image_of_the_session_owner()
    {
        var player = await ACustomPlayerAsync("ana@example.com", "avatars/ana-1", [1, 2, 3]);
        var caller = await SignInAsync(player);

        var result = await GetAsync(caller);

        Assert.True(result.IsSuccess);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Value);
    }

    [Fact]
    public async Task REQ019_never_returns_the_image_of_another_player()
    {
        var ana = await ACustomPlayerAsync("ana@example.com", "avatars/ana-1", [1, 1, 1]);
        var gui = await ACustomPlayerAsync("gui@example.com", "avatars/gui-1", [2, 2, 2]);
        var anaCaller = await SignInAsync(ana, "ana-token");
        var guiCaller = await SignInAsync(gui, "gui-token");

        Assert.Equal(new byte[] { 1, 1, 1 }, (await GetAsync(anaCaller)).Value);
        Assert.Equal(new byte[] { 2, 2, 2 }, (await GetAsync(guiCaller)).Value);
    }

    [Fact]
    public async Task REQ019_a_player_with_a_preset_avatar_gets_not_found_even_if_others_have_images()
    {
        await ACustomPlayerAsync("gui@example.com", "avatars/gui-1", [2, 2, 2]);
        var caller = await SignInAsync(APasswordPlayer(), "ana-token");

        var result = await GetAsync(caller);

        Assert.True(result.IsFailure);
        Assert.Equal(NotFound, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ019_a_custom_avatar_whose_image_is_missing_from_storage_is_not_found()
    {
        var player = await ACustomPlayerAsync("ana@example.com", "avatars/ana-1", null);
        var caller = await SignInAsync(player);

        var result = await GetAsync(caller);

        Assert.True(result.IsFailure);
        Assert.Equal(NotFound, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ011_without_a_session_the_read_is_unauthenticated()
    {
        var result = await GetAsync(null);

        Assert.True(result.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ011_a_player_removed_after_authentication_is_unauthenticated()
    {
        var player = await ACustomPlayerAsync("ana@example.com", "avatars/ana-1", [1, 2, 3]);
        var caller = await SignInAsync(player);
        await _players.Remove(player);
        await _uow.CommitAsync();

        var result = await GetAsync(caller);

        Assert.True(result.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
    }
}
