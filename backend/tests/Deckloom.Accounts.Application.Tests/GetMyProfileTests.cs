using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-045 (REQ-013, 016, 017, 045, 046): leitura do perfil do Jogador autenticado. RED: os tipos abaixo ainda nao existem.
// Forma esperada (convencao dos casos de uso protegidos, design.md linha 412: a identidade vem sempre do AuthenticatedPlayer):
//   sealed record GetMyProfileCommand(AuthenticatedPlayer? Caller)
//   sealed class GetMyProfile(IPlayerRepository, IFormatCatalog, IAvatarPresetCatalog)
//     Task<Result<Profile>> ExecuteAsync(GetMyProfileCommand, CancellationToken ct = default)
//   sealed record Profile(string DisplayName, string? Bio, FavoriteTcgItem FavoriteTcg,
//       IReadOnlyList<FormatOption> PreferredFormats, AvatarView Avatar, bool HasPassword, bool HasGoogle)
// Shape do DTO: tipo "Profile" de design.md linhas 1525-1530 (preferredFormats so ativos, REQ-046); FavoriteTcgItem(Id, Name)
// e FormatOption(Id, Name) ja existem (T-044) e batem com {id, name}. Sem email e sem hash (linha 1532).
// Falha unica: DomainError("unauthenticated") sem campo (linha 1428): Caller ausente ou Jogador removido.
// Avatar: AvatarView do T-030 (linha 366); preset => Alt "Avatar: {Label}"; custom => Kind "custom", PresetId null,
// ImageUrl "/api/v1/me/avatar/custom?v={StorageKey}" (linha 1520) e Alt "Seu avatar personalizado" (REFACTOR do T-045).
public class GetMyProfileTests
{
    private static readonly DomainError Unauthenticated = new("unauthenticated");

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

    public GetMyProfileTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
    }

    private GetMyProfile NewUseCase() => new(_players, _formats, _avatars);

    private async Task<Player> SeedAsync(Player player)
    {
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    private async Task<AuthenticatedPlayer> SignInAsync(Player player)
    {
        await SeedAsync(player);
        var session = Session.Start(player.Id, _tokenHasher.Hash("raw-token"), false, Now);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        var authenticate = new AuthenticateSession(_sessions, _players, _uow, _tokenHasher, _avatars, _clock);
        return (await authenticate.ExecuteAsync(new AuthenticateSessionCommand("raw-token"))).Value;
    }

    private Task<Result<Profile>> GetAsync(AuthenticatedPlayer? caller) =>
        NewUseCase().ExecuteAsync(new GetMyProfileCommand(caller));

    private List<FormatId> ActiveIds() =>
        _formats.ListActiveAsync().Result.Select(f => f.Id).ToList();

    private Player ACompletePlayer()
    {
        var player = APasswordPlayer();
        var update = player.UpdateProfile(
            "Ana", "Jogo Pauper e Commander", "mtg",
            [new FormatId("modern"), new FormatId("commander")], ActiveIds());
        Assert.True(update.IsSuccess);
        return player;
    }

    [Fact]
    public async Task REQ013_returns_the_profile_of_the_authenticated_player()
    {
        var caller = await SignInAsync(ACompletePlayer());

        var result = await GetAsync(caller);

        Assert.True(result.IsSuccess);
        var profile = result.Value;
        Assert.Equal("Ana", profile.DisplayName);
        Assert.Equal("Jogo Pauper e Commander", profile.Bio);
        Assert.Equal(new FavoriteTcgItem("mtg", "Magic: The Gathering"), profile.FavoriteTcg);
        Assert.Equal(
            [new FormatOption("modern", "Modern"), new FormatOption("commander", "Commander")],
            profile.PreferredFormats);
        Assert.True(profile.HasPassword);
        Assert.False(profile.HasGoogle);
    }

    [Fact]
    public async Task REQ013_a_google_only_player_reports_has_google_and_no_password()
    {
        var caller = await SignInAsync(AGooglePlayer("gui@example.com", "sub-1"));

        var result = await GetAsync(caller);

        Assert.Equal("Gui", result.Value.DisplayName);
        Assert.False(result.Value.HasPassword);
        Assert.True(result.Value.HasGoogle);
    }

    [Fact]
    public async Task REQ016_the_preset_avatar_uses_the_players_own_preset_and_not_the_catalog_default()
    {
        var caller = await SignInAsync(APasswordPlayer());

        var result = await GetAsync(caller);

        Assert.Equal(
            new AvatarView(AvatarView.PresetKind, "preset-01", "/avatars/preset-01.png", "Avatar: Dragao"),
            result.Value.Avatar);
    }

    [Fact]
    public async Task REQ016_the_custom_avatar_has_the_personalized_alt_and_the_versioned_image_url()
    {
        var player = APasswordPlayer();
        Assert.True(player.SetCustomAvatar("key-1").IsSuccess);
        var caller = await SignInAsync(player);

        var result = await GetAsync(caller);

        Assert.True(result.IsSuccess);
        Assert.Equal("custom", result.Value.Avatar.Kind);
        Assert.Null(result.Value.Avatar.PresetId);
        Assert.Equal("/api/v1/me/avatar/custom?v=key-1", result.Value.Avatar.ImageUrl);
        Assert.Equal("Seu avatar personalizado", result.Value.Avatar.Alt);
    }

    [Fact]
    public async Task REQ046_a_deactivated_format_disappears_from_the_profile_and_comes_back_when_reactivated()
    {
        var caller = await SignInAsync(ACompletePlayer());

        _formats.Deactivate(new FormatId("modern"));
        var hidden = await GetAsync(caller);
        _formats.Activate(new FormatId("modern"));
        var restored = await GetAsync(caller);

        Assert.Equal(["commander"], hidden.Value.PreferredFormats.Select(f => f.Id));
        Assert.Equal(["modern", "commander"], restored.Value.PreferredFormats.Select(f => f.Id));
    }

    [Fact]
    public async Task REQ017_a_profile_without_bio_and_without_formats_is_fine()
    {
        var caller = await SignInAsync(APasswordPlayer());

        var result = await GetAsync(caller);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Bio);
        Assert.Empty(result.Value.PreferredFormats);
        Assert.Equal("mtg", result.Value.FavoriteTcg.Id);
    }

    [Fact]
    public async Task REQ045_the_profile_does_not_carry_the_email_or_the_password_hash_in_any_value()
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);

        var profile = (await GetAsync(caller)).Value;

        var dump = string.Join("|", Flatten(profile));
        Assert.DoesNotContain(player.Email.Value, dump, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pbkdf2", dump, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(player.PasswordHash!.ToString()!, dump, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(Profile))]
    [InlineData(typeof(SessionSummary))]
    [InlineData(typeof(AvatarView))]
    public void REQ045_the_output_types_have_no_email_or_hash_property_at_any_depth(Type root)
    {
        foreach (var property in PublicPropertiesRecursively(root))
        {
            Assert.DoesNotContain("email", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(typeof(Email), property.PropertyType);
            Assert.NotEqual(typeof(PasswordHash), property.PropertyType);
            Assert.NotEqual(typeof(Player), property.PropertyType);
        }
    }

    [Fact]
    public async Task REQ011_without_a_session_the_profile_is_unauthenticated()
    {
        var result = await GetAsync(null);

        Assert.True(result.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ011_a_missing_session_token_never_reaches_a_profile()
    {
        await SignInAsync(ACompletePlayer());
        var authenticate = new AuthenticateSession(_sessions, _players, _uow, _tokenHasher, _avatars, _clock);
        var authenticated = await authenticate.ExecuteAsync(new AuthenticateSessionCommand(null));

        Assert.True(authenticated.IsFailure);
        Assert.Equal(Unauthenticated, authenticated.Error);
    }

    [Fact]
    public async Task REQ011_a_player_removed_after_authentication_is_unauthenticated()
    {
        var player = ACompletePlayer();
        var caller = await SignInAsync(player);
        await _players.Remove(player);
        await _uow.CommitAsync();

        var result = await GetAsync(caller);

        Assert.True(result.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ011_the_profile_is_the_one_of_the_caller_player_id_and_not_another_player()
    {
        var ana = await SignInAsync(ACompletePlayer());
        await SeedAsync(AGooglePlayer("gui@example.com", "sub-1"));

        var result = await GetAsync(ana);

        Assert.Equal("Ana", result.Value.DisplayName);
    }

    private static IEnumerable<System.Reflection.PropertyInfo> PublicPropertiesRecursively(
        Type type, HashSet<Type>? seen = null)
    {
        seen ??= [];
        if (!seen.Add(type))
        {
            yield break;
        }

        foreach (var property in type.GetProperties())
        {
            yield return property;
            var inner = property.PropertyType;
            if (inner.IsGenericType)
            {
                inner = inner.GetGenericArguments()[0];
            }

            if (inner.Namespace?.StartsWith("Deckloom", StringComparison.Ordinal) == true)
            {
                foreach (var nested in PublicPropertiesRecursively(inner, seen))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<string> Flatten(Profile profile) =>
    [
        profile.DisplayName,
        profile.Bio ?? string.Empty,
        profile.FavoriteTcg.Id,
        profile.FavoriteTcg.Name,
        .. profile.PreferredFormats.SelectMany(f => new[] { f.Id, f.Name }),
        profile.Avatar.Kind,
        profile.Avatar.PresetId ?? string.Empty,
        profile.Avatar.ImageUrl,
        profile.Avatar.Alt,
    ];
}
