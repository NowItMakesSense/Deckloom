using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

// T-146 (retro item 1): contrato de IAvatarStorage. O armazenamento e participante de commit: salvar e remover so valem
// quando a UoW confirma (ADR-0009), e o avatar e apagado junto com o Jogador (REQ-024). Tudo roda dentro de
// IUnitOfWork.ExecuteAsync SEM CommitAsync, e a leitura e feita em escopo novo. O dono da imagem e um Jogador cujo
// CustomAvatar tem a chave, no mesmo escopo (a porta nao recebe o PlayerId). Roda contra InMemoryAvatarStorage e
// PostgresAvatarStorage. Cobertura da porta: SaveAsync, ReadAsync, RemoveAsync.
public abstract class AvatarStorageContract : IAsyncDisposable
{
    // Limite do adapter (P2 do design, 256 KB).
    private const int MaxBytes = 262144;
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";
    private const string KeyA = "key-a-0000000000000000000";
    private const string KeyB = "key-b-0000000000000000000";

    private static readonly DateTimeOffset Now = AccountsContractFixture.ContractNow;

    private AccountsContractFixture? _fixture;

    protected abstract AccountsContractFixture CreateFixture();

    private AccountsContractFixture Fixture => _fixture ??= CreateFixture();

    public async ValueTask DisposeAsync()
    {
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    // Gerador unico de bytes de teste: cabecalho RIFF/WEBP seguido de enchimento determinado pela semente.
    private static byte[] Webp(int length, byte seed)
    {
        var bytes = new byte[length];
        "RIFF"u8.CopyTo(bytes);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        for (var i = 12; i < length; i++)
        {
            bytes[i] = (byte)(seed + i);
        }

        return bytes;
    }

    private static Player NewPlayer(string email, string? avatarKey)
    {
        var player = Player.RegisterWithPassword(
            Email.Create(email).Value,
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create("Ana Souza").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-1").Value;
        if (avatarKey is not null)
        {
            Assert.True(player.SetCustomAvatar(avatarKey).IsSuccess);
        }

        return player;
    }

    // Cria o Jogador com avatar customizado e grava a imagem no mesmo bloco, como o cadastro/troca de avatar faz.
    private async Task<Player> PersistPlayerWithAvatar(string key, byte[] image, string email = "ana@example.com")
    {
        var player = NewPlayer(email, key);
        await using var scope = Fixture.CreateScope();
        await scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            await scope.Players.Add(player, ct);
            await scope.Avatars.SaveAsync(key, image, ct);
        });
        return player;
    }

    private async Task<byte[]?> Read(string key)
    {
        await using var reader = Fixture.CreateScope();
        return await reader.Avatars.ReadAsync(key);
    }

    [Fact]
    public async Task Avatar_contract_Save_inside_ExecuteAsync_is_read_back_byte_for_byte_in_a_new_scope()
    {
        var image = Webp(2048, 1);
        await PersistPlayerWithAvatar(KeyA, image);

        Assert.Equal(image, await Read(KeyA));
    }

    [Fact]
    public async Task Avatar_contract_reading_an_unknown_key_or_a_player_without_custom_avatar_is_absent()
    {
        var player = NewPlayer("noavatar@example.com", null);
        await using (var scope = Fixture.CreateScope())
        {
            await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Add(player, ct));
        }

        Assert.Null(await Read(KeyA));
        Assert.Null(await Read("never-saved"));
    }

    [Fact]
    public async Task Avatar_contract_Save_on_an_existing_key_replaces_the_image()
    {
        var player = await PersistPlayerWithAvatar(KeyA, Webp(1024, 1));
        var replacement = Webp(3000, 9);
        await using var scope = Fixture.CreateScope();
        var loaded = await scope.Players.GetById(player.Id);

        await scope.UnitOfWork.ExecuteAsync(ct => scope.Avatars.SaveAsync(KeyA, replacement, ct));

        Assert.NotNull(loaded);
        Assert.Equal(replacement, await Read(KeyA));
    }

    [Fact]
    public async Task Avatar_contract_replacing_the_avatar_removes_the_previous_image_REQ_021()
    {
        var player = await PersistPlayerWithAvatar(KeyA, Webp(1024, 1));
        var next = Webp(1500, 5);
        await using var scope = Fixture.CreateScope();
        var loaded = await scope.Players.GetById(player.Id);

        await scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            Assert.True(loaded!.SetCustomAvatar(KeyB).IsSuccess);
            await scope.Avatars.SaveAsync(KeyB, next, ct);
            await scope.Avatars.RemoveAsync(KeyA, ct);
            await scope.Players.Save(loaded, ct);
        });

        Assert.Null(await Read(KeyA));
        Assert.Equal(next, await Read(KeyB));
    }

    [Fact]
    public async Task Avatar_contract_Remove_then_Read_is_absent_and_removing_an_unknown_key_is_not_an_error()
    {
        await PersistPlayerWithAvatar(KeyA, Webp(1024, 1));
        await using var scope = Fixture.CreateScope();

        await scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            await scope.Avatars.RemoveAsync(KeyA, ct);
            await scope.Avatars.RemoveAsync("never-saved", ct);
        });

        Assert.Null(await Read(KeyA));
    }

    [Fact]
    public async Task Avatar_contract_removing_the_player_removes_the_avatar_REQ_024()
    {
        var player = await PersistPlayerWithAvatar(KeyA, Webp(1024, 1));
        await using var scope = Fixture.CreateScope();
        var loaded = await scope.Players.GetById(player.Id);

        await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Remove(loaded!, ct));

        Assert.Null(await Read(KeyA));
    }

    [Fact]
    public async Task Avatar_contract_a_block_that_throws_saves_nothing_and_the_previous_image_remains()
    {
        var previous = Webp(1024, 1);
        var player = await PersistPlayerWithAvatar(KeyA, previous);
        await using var scope = Fixture.CreateScope();
        var loaded = await scope.Players.GetById(player.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            Assert.True(loaded!.SetCustomAvatar(KeyB).IsSuccess);
            await scope.Avatars.SaveAsync(KeyB, Webp(500, 7), ct);
            await scope.Avatars.SaveAsync(KeyA, Webp(700, 3), ct);
            await scope.Avatars.RemoveAsync(KeyA, ct);
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal(previous, await Read(KeyA));
        Assert.Null(await Read(KeyB));
    }

    [Fact]
    public async Task Avatar_contract_Read_returns_a_copy_that_does_not_alter_the_stored_image()
    {
        var image = Webp(1024, 1);
        await PersistPlayerWithAvatar(KeyA, image);
        var first = await Read(KeyA);
        first![20] ^= 0xFF;

        Assert.Equal(image, await Read(KeyA));
    }

    [Fact]
    public async Task Avatar_contract_Save_keeps_its_own_copy_of_the_bytes_it_was_given()
    {
        var image = Webp(1024, 1);
        var expected = image.ToArray();
        var player = NewPlayer("copy@example.com", KeyA);
        await using (var scope = Fixture.CreateScope())
        {
            await scope.UnitOfWork.ExecuteAsync(async ct =>
            {
                await scope.Players.Add(player, ct);
                await scope.Avatars.SaveAsync(KeyA, image, ct);
                image[20] ^= 0xFF;
            });
        }

        Assert.Equal(expected, await Read(KeyA));
    }

    [Fact]
    public async Task Avatar_contract_an_image_of_exactly_the_limit_is_saved()
    {
        var image = Webp(MaxBytes, 1);
        await PersistPlayerWithAvatar(KeyA, image);

        Assert.Equal(image, await Read(KeyA));
    }

    [Fact]
    public async Task Avatar_contract_an_image_above_the_limit_is_refused_with_ArgumentException_and_nothing_is_saved()
    {
        var player = NewPlayer("big@example.com", KeyA);
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<ArgumentException>(() => scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            await scope.Players.Add(player, ct);
            await scope.Avatars.SaveAsync(KeyA, Webp(MaxBytes + 1, 1), ct);
        }));

        Assert.Null(await Read(KeyA));
    }

    [Fact]
    public async Task Avatar_contract_a_null_key_is_refused_with_ArgumentNullException_and_nothing_is_saved()
    {
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<ArgumentNullException>(() => scope.UnitOfWork.ExecuteAsync(
            ct => scope.Avatars.SaveAsync(null!, Webp(1024, 1), ct)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   	 ")]
    public async Task Avatar_contract_an_empty_or_blank_key_is_refused_with_ArgumentException_and_nothing_is_saved(string key)
    {
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<ArgumentException>(() => scope.UnitOfWork.ExecuteAsync(
            ct => scope.Avatars.SaveAsync(key, Webp(1024, 1), ct)));

        Assert.Null(await Read(key));
    }

    [Fact]
    public async Task Avatar_contract_a_key_that_belongs_to_no_player_of_the_scope_is_refused_with_InvalidOperationException()
    {
        var player = NewPlayer("owner@example.com", KeyA);
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            await scope.Players.Add(player, ct);
            await scope.Avatars.SaveAsync(KeyB, Webp(1024, 1), ct);
        }));

        Assert.Null(await Read(KeyB));
        Assert.Null(await Read(KeyA));
    }

    // T-161 (R-45): o dono so conta se foi lido ou adicionado no escopo; um Jogador confirmado mas nao carregado nao vale.
    [Fact]
    public async Task Avatar_contract_a_committed_player_not_loaded_in_the_scope_does_not_own_the_key_R_45()
    {
        await PersistPlayerWithAvatar(KeyA, Webp(1024, 1));
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(
            ct => scope.Avatars.SaveAsync(KeyA, Webp(2048, 2), ct)));

        Assert.Equal(Webp(1024, 1), await Read(KeyA));
    }

    [Fact]
    public async Task Avatar_contract_a_player_read_in_the_scope_owns_the_key_R_45()
    {
        var player = await PersistPlayerWithAvatar(KeyA, Webp(1024, 1));
        var replacement = Webp(2048, 2);
        await using var scope = Fixture.CreateScope();
        var loaded = await scope.Players.GetById(player.Id);

        await scope.UnitOfWork.ExecuteAsync(ct => scope.Avatars.SaveAsync(KeyA, replacement, ct));

        Assert.NotNull(loaded);
        Assert.Equal(replacement, await Read(KeyA));
    }

    [Fact]
    public async Task Avatar_contract_a_key_with_no_player_in_the_scope_at_all_is_refused_with_InvalidOperationException()
    {
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(
            ct => scope.Avatars.SaveAsync(KeyA, Webp(1024, 1), ct)));

        Assert.Null(await Read(KeyA));
    }

    // T-166 (R-51): o EfUnitOfWork limpa o ChangeTracker no rollback (EfUnitOfWork.cs:96-97) e desanexa o Jogador removido
    // depois do SaveChanges; o dono lido so dentro do bloco revertido ou removido deixa de valer, como no EF.
    [Fact]
    public async Task Avatar_contract_R_51_saving_after_a_rolled_back_block_that_read_the_owner_is_refused()
    {
        var player = await PersistPlayerWithAvatar(KeyA, Webp(1024, 1));
        await using var scope = Fixture.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            Assert.NotNull(await scope.Players.GetById(player.Id, ct));
            throw new InvalidOperationException("boom");
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(
            ct => scope.Avatars.SaveAsync(KeyA, Webp(2048, 2), ct)));

        Assert.Equal(Webp(1024, 1), await Read(KeyA));
    }

    [Fact]
    public async Task Avatar_contract_R_51_saving_for_a_player_whose_removal_was_confirmed_is_refused()
    {
        var player = await PersistPlayerWithAvatar(KeyA, Webp(1024, 1));
        await using var scope = Fixture.CreateScope();
        var loaded = await scope.Players.GetById(player.Id);
        await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Remove(loaded!, ct));

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(
            ct => scope.Avatars.SaveAsync(KeyA, Webp(2048, 2), ct)));

        Assert.Null(await Read(KeyA));
    }

    [Fact]
    public async Task Avatar_contract_R_51_the_player_read_again_after_the_rollback_owns_the_key_and_saves()
    {
        var player = await PersistPlayerWithAvatar(KeyA, Webp(1024, 1));
        var replacement = Webp(2048, 2);
        await using var scope = Fixture.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            Assert.NotNull(await scope.Players.GetById(player.Id, ct));
            throw new InvalidOperationException("boom");
        }));
        Assert.NotNull(await scope.Players.GetById(player.Id));

        await scope.UnitOfWork.ExecuteAsync(ct => scope.Avatars.SaveAsync(KeyA, replacement, ct));

        Assert.Equal(replacement, await Read(KeyA));
    }

    // Decisao do usuario (T-166): remover um Jogador retira so ele dos rastreados; os demais continuam donos.
    [Fact]
    public async Task Avatar_contract_R_51_removing_one_player_keeps_the_other_loaded_player_as_owner_of_its_key()
    {
        var removed = await PersistPlayerWithAvatar(KeyA, Webp(1024, 1), "removed@example.com");
        var kept = await PersistPlayerWithAvatar(KeyB, Webp(1024, 2), "kept@example.com");
        var replacement = Webp(2048, 3);
        await using var scope = Fixture.CreateScope();
        var loadedRemoved = await scope.Players.GetById(removed.Id);
        var loadedKept = await scope.Players.GetById(kept.Id);
        await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Remove(loadedRemoved!, ct));

        await scope.UnitOfWork.ExecuteAsync(ct => scope.Avatars.SaveAsync(KeyB, replacement, ct));

        Assert.NotNull(loadedKept);
        Assert.Equal(replacement, await Read(KeyB));
        Assert.Null(await Read(KeyA));
    }
}
