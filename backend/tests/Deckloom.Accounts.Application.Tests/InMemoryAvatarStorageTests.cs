using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-028 (habilitadora; REQ-019, REQ-021, REQ-024; ADR-0009): IAvatarStorage salva, le e remove avatar customizado por
// chave. SaveAsync(string key, byte[] image, ct), ReadAsync(string key, ct) : byte[]? (nulo se ausente),
// RemoveAsync(string key, ct) (no-op se ausente). InMemoryAvatarStorage(InMemoryUnitOfWork) participa da unidade de
// trabalho: Save/Remove sao mudancas pendentes, visiveis em ReadAsync so apos CommitAsync/ExecuteAsync bem-sucedido;
// o descarte (excecao no bloco ou UniqueConstraintViolationException no commit) as reverte.
public class InMemoryAvatarStorageTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemoryAvatarStorage _storage;

    public InMemoryAvatarStorageTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _storage = new InMemoryAvatarStorage(_uow);
    }

    // T-161 (R-45): gravar exige um dono no escopo; adiciona ao escopo um Jogador cujo avatar customizado tem a chave.
    private async Task Own(string key)
    {
        var player = PlayerFixtures.APasswordPlayer($"{key}@example.com");
        Assert.True(player.SetCustomAvatar(key).IsSuccess);
        await _players.Add(player);
    }

    private async Task Seed(string key, params byte[] image)
    {
        await Own(key);
        await _storage.SaveAsync(key, image);
        await _uow.CommitAsync();
    }

    [Fact]
    public async Task REQ019_Read_of_an_unknown_key_returns_null()
    {
        Assert.Null(await _storage.ReadAsync("k1"));
    }

    [Fact]
    public async Task REQ019_Save_then_commit_then_Read_returns_the_same_bytes()
    {
        byte[] image = [1, 2, 3];

        await Seed("k1", image);

        Assert.Equal(image, await _storage.ReadAsync("k1"));
    }

    [Fact]
    public async Task REQ019_Save_is_pending_until_commit()
    {
        await Own("k1");
        await _storage.SaveAsync("k1", [1]);
        Assert.Null(await _storage.ReadAsync("k1"));

        await _uow.CommitAsync();

        Assert.Equal(new byte[] { 1 }, await _storage.ReadAsync("k1"));
    }

    [Fact]
    public async Task REQ019_Save_stores_a_copy_isolated_from_later_changes_to_the_input_and_the_output()
    {
        byte[] image = [1, 2, 3];
        await Own("k1");
        await _storage.SaveAsync("k1", image);
        image[0] = 9;
        await _uow.CommitAsync();

        var read = await _storage.ReadAsync("k1");
        read![1] = 9;

        Assert.Equal(new byte[] { 1, 2, 3 }, await _storage.ReadAsync("k1"));
    }

    [Fact]
    public async Task REQ019_Keys_are_independent()
    {
        await Seed("k1", 1);
        await Seed("k2", 2);

        Assert.Equal(new byte[] { 1 }, await _storage.ReadAsync("k1"));
        Assert.Equal(new byte[] { 2 }, await _storage.ReadAsync("k2"));
    }

    [Fact]
    public async Task REQ021_Remove_is_pending_until_commit_and_deletes_only_that_key()
    {
        await Seed("k1", 1);
        await Seed("k2", 2);

        await _storage.RemoveAsync("k1");
        Assert.Equal(new byte[] { 1 }, await _storage.ReadAsync("k1"));

        await _uow.CommitAsync();

        Assert.Null(await _storage.ReadAsync("k1"));
        Assert.Equal(new byte[] { 2 }, await _storage.ReadAsync("k2"));
    }

    [Fact]
    public async Task REQ024_Remove_of_an_unknown_key_is_a_no_op()
    {
        await _storage.RemoveAsync("missing");
        await _uow.CommitAsync();

        Assert.Null(await _storage.ReadAsync("missing"));
    }

    [Fact]
    public async Task REQ019_Save_on_an_existing_key_replaces_the_image_on_commit()
    {
        await Seed("k1", 1);

        await _storage.SaveAsync("k1", [7, 7]);
        await _uow.CommitAsync();

        Assert.Equal(new byte[] { 7, 7 }, await _storage.ReadAsync("k1"));
    }

    [Fact]
    public async Task REQ019_ExecuteAsync_applies_the_save_when_the_block_succeeds()
    {
        await Own("k1");
        await _uow.ExecuteAsync(ct => _storage.SaveAsync("k1", [4], ct));

        Assert.Equal(new byte[] { 4 }, await _storage.ReadAsync("k1"));
    }

    [Fact]
    public async Task REQ019_Rollback_discards_the_saved_image_when_the_block_throws()
    {
        await Own("k1");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _storage.SaveAsync("k1", [4], ct);
                throw new InvalidOperationException();
            }));
        await _uow.CommitAsync();

        Assert.Null(await _storage.ReadAsync("k1"));
    }

    [Fact]
    public async Task REQ021_Swapping_custom_for_preset_removes_the_image_and_the_rollback_keeps_it()
    {
        await Seed("custom-1", 1, 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _storage.RemoveAsync("custom-1", ct);
                throw new InvalidOperationException("later step failed");
            }));
        await _uow.CommitAsync();

        Assert.Equal(new byte[] { 1, 2 }, await _storage.ReadAsync("custom-1"));
    }

    [Fact]
    public async Task REQ021_Swapping_custom_for_preset_removes_the_image_when_the_block_succeeds()
    {
        await Seed("custom-1", 1, 2);

        await _uow.ExecuteAsync(ct => _storage.RemoveAsync("custom-1", ct));

        Assert.Null(await _storage.ReadAsync("custom-1"));
    }

    [Fact]
    public async Task REQ019_Replacing_a_custom_avatar_rolls_back_both_the_new_save_and_the_old_removal()
    {
        await Seed("old", 1);
        await Own("new");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _storage.SaveAsync("new", [2], ct);
                await _storage.RemoveAsync("old", ct);
                throw new InvalidOperationException();
            }));

        Assert.Null(await _storage.ReadAsync("new"));
        Assert.Equal(new byte[] { 1 }, await _storage.ReadAsync("old"));
    }

    // T-161 (R-45): sem fonte de Jogadores no escopo a checagem nunca e pulada; gravar lanca.
    [Fact]
    public async Task R45_Save_in_a_unit_of_work_without_a_player_repository_throws_instead_of_skipping_the_owner_check()
    {
        var storage = new InMemoryAvatarStorage(new InMemoryUnitOfWork());

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync("k1", [1]));
    }

    // T-161 (R-45): o dono e a uniao dos Jogadores vistos por todos os repositorios da mesma UoW.
    [Fact]
    public async Task R45_two_player_repositories_in_the_same_unit_of_work_contribute_the_union_of_owners()
    {
        var first = new InMemoryPlayerRepository(_uow);
        var second = new InMemoryPlayerRepository(_uow);
        var ana = PlayerFixtures.APasswordPlayer();
        Assert.True(ana.SetCustomAvatar("key-ana").IsSuccess);
        var bia = PlayerFixtures.APasswordPlayer("bia@example.com");
        Assert.True(bia.SetCustomAvatar("key-bia").IsSuccess);

        await _uow.ExecuteAsync(async ct =>
        {
            await first.Add(ana, ct);
            await second.Add(bia, ct);
            await _storage.SaveAsync("key-ana", [1], ct);
            await _storage.SaveAsync("key-bia", [2], ct);
        });

        Assert.Equal(new byte[] { 1 }, await _storage.ReadAsync("key-ana"));
        Assert.Equal(new byte[] { 2 }, await _storage.ReadAsync("key-bia"));
    }

    [Fact]
    public async Task REQ024_Rollback_on_a_uniqueness_failure_at_commit_reverts_save_and_remove()
    {
        var players = new InMemoryPlayerRepository(_uow);
        await players.Add(PlayerFixtures.APasswordPlayer());
        await _uow.CommitAsync();
        _storage.SeedCommitted("old", [1]);

        // T-146: a chave nova precisa de um dono no escopo; o dono duplica o e-mail e provoca a falha no commit.
        var duplicate = PlayerFixtures.APasswordPlayer();
        Assert.True(duplicate.SetCustomAvatar("new").IsSuccess);
        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await players.Add(duplicate, ct);
                await _storage.SaveAsync("new", [2], ct);
                await _storage.RemoveAsync("old", ct);
            }));

        Assert.Null(await _storage.ReadAsync("new"));
        Assert.Equal(new byte[] { 1 }, await _storage.ReadAsync("old"));
    }
}
