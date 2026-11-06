using System.Reflection;
using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-020 (REQ-023, REQ-024, REQ-026, REQ-042; J9): exclusao de conta e Reautenticacao no agregado Player.
// Design (emenda 2026-10-23, "Exclusao de conta"):
//   Delete(bool confirmed, Reauthentication? reauthentication, DateTimeOffset now) : Result<Player>.
//   Ordem fixa, so o primeiro erro: (1) confirmed = false -> required (Field "confirm");
//   (2) reautenticacao de outro Jogador -> invalid_proof; (3) tipo errado para o tipo de conta, nula ou
//   ReauthenticatedAt nulo -> reauth_required; (4) ReauthenticatedAt < now - 5 min -> reauth_expired (5:00 aceito).
//   Falha: Field nulo (exceto confirm), sem mudanca de estado e sem evento. Sucesso: so PlayerDeleted(PlayerId).
//   Hierarquia fechada: abstract Reauthentication (private protected) com PasswordProof e RecentGoogleLogin sealed,
//   construtores internal, igualdade por valor. Janela de 5 min: constante publica unica (Reauthentication.Window).
public class DeletePlayerTests
{
    private const string Current = "2026-10-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static Player APasswordPlayer(string email = "ana@example.com")
    {
        var player = Player.RegisterWithPassword(
            Email.Create(email).Value,
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create("Ana").Value,
            Current,
            Current,
            true,
            true,
            Now,
            "preset-01").Value;
        player.ClearDomainEvents();
        return player;
    }

    private static Player AGoogleOnlyPlayer(string email = "ana@example.com")
    {
        var player = Player.RegisterWithGoogle(
            Email.Create(email).Value,
            ExternalIdentity.Create("google", "google-subject-1").Value,
            DisplayName.Create("Ana").Value,
            Current,
            Current,
            true,
            true,
            Now,
            "preset-01").Value;
        player.ClearDomainEvents();
        return player;
    }

    private static Player ALinkedPlayer()
    {
        var player = APasswordPlayer();
        Assert.True(player.LinkGoogle(
            ExternalIdentity.Create("google", "google-subject-1").Value,
            Email.Create("ana@example.com").Value,
            new PasswordProof(player.Id)).IsSuccess);
        player.ClearDomainEvents();
        return player;
    }

    private static PasswordProof ProofFor(Player player) => new(player.Id);

    private static RecentGoogleLogin LoginFor(Player player, DateTimeOffset? at) => new(player.Id, at);

    private static void AssertSingleError(Result<Player> result, string code, string? field = null)
    {
        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(field, error.Field);
    }

    private static PlayerState Snapshot(Player player) =>
        new(
            player.Email,
            player.PasswordHash,
            player.ExternalIdentity,
            player.DisplayName,
            player.Avatar,
            player.Bio,
            player.FavoriteTcg,
            player.Formats,
            player.TermsAcceptance);

    private static void AssertUntouched(Player player, PlayerState before)
    {
        Assert.Equal(before.Email, player.Email);
        Assert.Same(before.PasswordHash, player.PasswordHash);
        Assert.Same(before.ExternalIdentity, player.ExternalIdentity);
        Assert.Same(before.DisplayName, player.DisplayName);
        Assert.Same(before.Avatar, player.Avatar);
        Assert.Equal(before.Bio, player.Bio);
        Assert.Equal(before.FavoriteTcg, player.FavoriteTcg);
        Assert.Equal(before.Formats, player.Formats);
        Assert.Equal(before.TermsAcceptance, player.TermsAcceptance);
        Assert.Null(player.PasswordResetToken);
        Assert.Empty(player.DomainEvents);
    }

    // Confirmacao (REQ-023)

    [Fact]
    public void REQ_023_without_confirmation_a_password_player_gets_required_on_confirm_and_nothing_changes()
    {
        var player = APasswordPlayer();
        var before = Snapshot(player);

        var result = player.Delete(false, ProofFor(player), Now);

        AssertSingleError(result, "required", "confirm");
        AssertUntouched(player, before);
    }

    [Fact]
    public void REQ_023_without_confirmation_a_google_player_gets_required_on_confirm_even_with_a_fresh_login()
    {
        var player = AGoogleOnlyPlayer();

        var result = player.Delete(false, LoginFor(player, Now), Now);

        AssertSingleError(result, "required", "confirm");
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_023_confirmation_is_checked_first_even_without_any_reauthentication()
    {
        var player = APasswordPlayer();

        var result = player.Delete(false, null, Now);

        AssertSingleError(result, "required", "confirm");
    }

    // Conta com senha (REQ-023, REQ-026)

    [Fact]
    public void REQ_023_a_password_player_with_confirmation_and_a_password_proof_is_deleted()
    {
        var player = APasswordPlayer();

        var result = player.Delete(true, ProofFor(player), Now);

        Assert.True(result.IsSuccess);
        Assert.Same(player, result.Value);
    }

    [Fact]
    public void REQ_026_a_password_proof_has_no_time_window()
    {
        var player = APasswordPlayer();

        var result = player.Delete(true, ProofFor(player), Now.AddYears(5));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void REQ_026_a_linked_player_with_a_local_password_still_needs_the_password_proof()
    {
        var player = ALinkedPlayer();

        var result = player.Delete(true, LoginFor(player, Now), Now);

        AssertSingleError(result, "reauth_required");
        Assert.Empty(player.DomainEvents);
        Assert.NotNull(player.ExternalIdentity);
    }

    [Fact]
    public void REQ_026_a_linked_player_with_a_password_proof_is_deleted()
    {
        var player = ALinkedPlayer();

        var result = player.Delete(true, ProofFor(player), Now);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void REQ_026_a_password_player_without_reauthentication_gets_reauth_required()
    {
        var player = APasswordPlayer();

        var result = player.Delete(true, null, Now);

        AssertSingleError(result, "reauth_required");
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_026_a_password_player_given_a_fresh_google_login_gets_reauth_required()
    {
        var player = APasswordPlayer();

        var result = player.Delete(true, LoginFor(player, Now), Now);

        AssertSingleError(result, "reauth_required");
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_026_a_password_player_given_an_expired_google_login_still_gets_reauth_required_not_expired()
    {
        var player = APasswordPlayer();

        var result = player.Delete(true, LoginFor(player, Now.AddHours(-2)), Now);

        AssertSingleError(result, "reauth_required");
    }

    // Conta sem senha local (REQ-042)

    [Fact]
    public void REQ_042_a_google_player_with_a_login_from_two_minutes_ago_and_confirmation_is_deleted()
    {
        var player = AGoogleOnlyPlayer();

        var result = player.Delete(true, LoginFor(player, Now.AddMinutes(-2)), Now);

        Assert.True(result.IsSuccess);
        Assert.Same(player, result.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    [InlineData(299)]
    [InlineData(300)]
    public void REQ_042_a_login_up_to_exactly_five_minutes_old_is_accepted(int secondsAgo)
    {
        var player = AGoogleOnlyPlayer();

        var result = player.Delete(true, LoginFor(player, Now.AddSeconds(-secondsAgo)), Now);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(3600)]
    [InlineData(86400)]
    public void REQ_042_a_login_older_than_five_minutes_is_reauth_expired_and_nothing_changes(int secondsAgo)
    {
        var player = AGoogleOnlyPlayer();
        var before = Snapshot(player);

        var result = player.Delete(true, LoginFor(player, Now.AddSeconds(-secondsAgo)), Now);

        AssertSingleError(result, "reauth_expired");
        AssertUntouched(player, before);
    }

    [Fact]
    public void REQ_042_the_five_minute_boundary_compares_instants_not_offsets()
    {
        var player = AGoogleOnlyPlayer();
        var exactlyFiveMinutesAgoInAnotherOffset = (Now - TimeSpan.FromMinutes(5)).ToOffset(TimeSpan.FromHours(-3));

        var result = player.Delete(true, LoginFor(player, exactlyFiveMinutesAgoInAnotherOffset), Now);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void REQ_042_a_google_player_that_never_reauthenticated_gets_reauth_required()
    {
        var player = AGoogleOnlyPlayer();

        var result = player.Delete(true, LoginFor(player, null), Now);

        AssertSingleError(result, "reauth_required");
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_042_a_google_player_without_reauthentication_gets_reauth_required()
    {
        var player = AGoogleOnlyPlayer();

        var result = player.Delete(true, null, Now);

        AssertSingleError(result, "reauth_required");
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_042_a_google_player_given_a_password_proof_gets_reauth_required()
    {
        var player = AGoogleOnlyPlayer();

        var result = player.Delete(true, ProofFor(player), Now);

        AssertSingleError(result, "reauth_required");
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_042_the_reauthentication_window_is_a_single_public_five_minute_constant()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), Reauthentication.Window);
    }

    // Reautenticacao de outro Jogador

    [Fact]
    public void REQ_026_a_password_proof_of_another_player_is_invalid_proof_and_nothing_changes()
    {
        var player = APasswordPlayer();
        var before = Snapshot(player);
        var other = APasswordPlayer("bia@example.com");

        var result = player.Delete(true, ProofFor(other), Now);

        AssertSingleError(result, "invalid_proof");
        AssertUntouched(player, before);
    }

    [Fact]
    public void REQ_042_a_google_login_of_another_player_is_invalid_proof_and_nothing_changes()
    {
        var player = AGoogleOnlyPlayer();
        var before = Snapshot(player);
        var other = AGoogleOnlyPlayer("bia@example.com");

        var result = player.Delete(true, LoginFor(other, Now), Now);

        AssertSingleError(result, "invalid_proof");
        AssertUntouched(player, before);
    }

    // Precedencia

    [Fact]
    public void REQ_023_required_wins_over_invalid_proof()
    {
        var player = APasswordPlayer();
        var other = APasswordPlayer("bia@example.com");

        var result = player.Delete(false, ProofFor(other), Now);

        AssertSingleError(result, "required", "confirm");
    }

    [Fact]
    public void REQ_023_required_wins_over_reauth_expired()
    {
        var player = AGoogleOnlyPlayer();

        var result = player.Delete(false, LoginFor(player, Now.AddHours(-1)), Now);

        AssertSingleError(result, "required", "confirm");
    }

    [Fact]
    public void REQ_026_invalid_proof_wins_over_reauth_required_for_the_wrong_type_from_another_player()
    {
        var player = APasswordPlayer();
        var other = AGoogleOnlyPlayer("bia@example.com");

        var result = player.Delete(true, LoginFor(other, Now), Now);

        AssertSingleError(result, "invalid_proof");
    }

    [Fact]
    public void REQ_026_invalid_proof_wins_over_reauth_required_for_a_google_account_given_a_password_proof_of_another()
    {
        var player = AGoogleOnlyPlayer();
        var other = APasswordPlayer("bia@example.com");

        var result = player.Delete(true, ProofFor(other), Now);

        AssertSingleError(result, "invalid_proof");
    }

    [Fact]
    public void REQ_042_invalid_proof_wins_over_reauth_expired()
    {
        var player = AGoogleOnlyPlayer();
        var other = AGoogleOnlyPlayer("bia@example.com");

        var result = player.Delete(true, LoginFor(other, Now.AddHours(-1)), Now);

        AssertSingleError(result, "invalid_proof");
    }

    [Fact]
    public void REQ_042_invalid_proof_wins_over_a_missing_reauthentication_timestamp()
    {
        var player = AGoogleOnlyPlayer();
        var other = AGoogleOnlyPlayer("bia@example.com");

        var result = player.Delete(true, LoginFor(other, null), Now);

        AssertSingleError(result, "invalid_proof");
    }

    [Fact]
    public void REQ_042_reauth_required_for_a_missing_timestamp_is_reported_instead_of_reauth_expired()
    {
        var player = AGoogleOnlyPlayer();

        var result = player.Delete(true, LoginFor(player, null), Now.AddYears(1));

        AssertSingleError(result, "reauth_required");
    }

    // Efeitos

    [Fact]
    public void REQ_024_deleting_emits_only_PlayerDeleted_with_the_player_id()
    {
        var player = APasswordPlayer();

        player.Delete(true, ProofFor(player), Now);

        var deleted = Assert.IsType<PlayerDeleted>(Assert.Single(player.DomainEvents));
        Assert.Equal(player.Id, deleted.PlayerId);
    }

    [Fact]
    public void REQ_024_deleting_a_google_player_emits_only_PlayerDeleted_with_the_player_id()
    {
        var player = AGoogleOnlyPlayer();

        player.Delete(true, LoginFor(player, Now), Now);

        var deleted = Assert.IsType<PlayerDeleted>(Assert.Single(player.DomainEvents));
        Assert.Equal(player.Id, deleted.PlayerId);
    }

    [Fact]
    public void REQ_024_deleting_does_not_change_any_other_state_of_the_aggregate()
    {
        var player = APasswordPlayer();
        var before = Snapshot(player);

        player.Delete(true, ProofFor(player), Now);

        Assert.Equal(before.Email, player.Email);
        Assert.Same(before.PasswordHash, player.PasswordHash);
        Assert.Same(before.ExternalIdentity, player.ExternalIdentity);
        Assert.Same(before.DisplayName, player.DisplayName);
        Assert.Same(before.Avatar, player.Avatar);
        Assert.Equal(before.TermsAcceptance, player.TermsAcceptance);
    }

    [Fact]
    public void REQ_026_a_failed_attempt_can_be_retried_with_a_valid_reauthentication()
    {
        var player = AGoogleOnlyPlayer();
        player.Delete(true, LoginFor(player, Now.AddHours(-1)), Now);

        var result = player.Delete(true, LoginFor(player, Now), Now);

        Assert.True(result.IsSuccess);
        Assert.IsType<PlayerDeleted>(Assert.Single(player.DomainEvents));
    }

    [Fact]
    public void REQ_024_the_event_carries_only_the_player_id()
    {
        var properties = typeof(PlayerDeleted)
            .GetProperties()
            .Where(property => property.DeclaringType == typeof(PlayerDeleted))
            .Select(property => (property.Name, property.PropertyType))
            .ToArray();

        Assert.Equal([("PlayerId", typeof(PlayerId))], properties);
    }

    // Forma: hierarquia fechada

    [Fact]
    public void REQ_026_Reauthentication_is_abstract_and_has_exactly_the_two_sealed_subtypes()
    {
        var baseType = typeof(Reauthentication);
        var derived = baseType.Assembly.GetTypes()
            .Where(t => t != baseType && baseType.IsAssignableFrom(t))
            .ToList();

        Assert.True(baseType.IsAbstract);
        Assert.Equal(2, derived.Count);
        Assert.Contains(typeof(PasswordProof), derived);
        Assert.Contains(typeof(RecentGoogleLogin), derived);
        Assert.All(derived, t => Assert.True(t.IsSealed, $"{t.Name} must be sealed"));
    }

    [Fact]
    public void REQ_026_no_type_of_the_hierarchy_has_a_public_constructor()
    {
        foreach (var type in new[] { typeof(Reauthentication), typeof(PasswordProof), typeof(RecentGoogleLogin) })
        {
            Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }
    }

    [Fact]
    public void REQ_026_no_Reauthentication_constructor_is_reachable_by_types_derived_in_other_assemblies()
    {
        var constructors = typeof(Reauthentication).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotEmpty(constructors);
        Assert.All(constructors, c =>
        {
            Assert.False(c.IsPublic, $"{c} must not be public");
            Assert.False(c.IsFamily, $"{c} must not be protected");
            Assert.False(c.IsFamilyOrAssembly, $"{c} must not be protected internal");
        });
    }

    [Fact]
    public void REQ_026_the_subtype_constructors_are_internal()
    {
        foreach (var type in new[] { typeof(PasswordProof), typeof(RecentGoogleLogin) })
        {
            var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotEmpty(constructors);
            Assert.All(constructors, c => Assert.True(c.IsAssembly, $"{type.Name} constructor {c} must be internal"));
        }
    }

    [Fact]
    public void REQ_042_RecentGoogleLogin_exposes_the_player_id_and_the_nullable_timestamp()
    {
        var id = new PlayerId(Guid.NewGuid());
        var at = Now.AddMinutes(-1);

        var withValue = new RecentGoogleLogin(id, at);
        var without = new RecentGoogleLogin(id, null);

        Assert.Equal(id, withValue.PlayerId);
        Assert.Equal(at, withValue.ReauthenticatedAt);
        Assert.Null(without.ReauthenticatedAt);
        Assert.Equal(typeof(DateTimeOffset?), typeof(RecentGoogleLogin).GetProperty("ReauthenticatedAt")!.PropertyType);
    }

    // Forma: igualdade por valor

    [Fact]
    public void REQ_026_PasswordProof_has_value_equality_by_player_id()
    {
        var id = new PlayerId(Guid.NewGuid());
        var same = new PasswordProof(id);

        Assert.Equal(new PasswordProof(id), same);
        Assert.Equal(new PasswordProof(id).GetHashCode(), same.GetHashCode());
        Assert.NotEqual(new PasswordProof(id), new PasswordProof(new PlayerId(Guid.NewGuid())));
        Assert.False(same.Equals(null));
    }

    [Fact]
    public void REQ_042_RecentGoogleLogin_has_value_equality_by_player_id_and_timestamp()
    {
        var id = new PlayerId(Guid.NewGuid());
        var at = Now.AddMinutes(-1);

        Assert.Equal(new RecentGoogleLogin(id, at), new RecentGoogleLogin(id, at));
        Assert.Equal(new RecentGoogleLogin(id, at).GetHashCode(), new RecentGoogleLogin(id, at).GetHashCode());
        Assert.Equal(new RecentGoogleLogin(id, null), new RecentGoogleLogin(id, null));
        Assert.Equal(new RecentGoogleLogin(id, null).GetHashCode(), new RecentGoogleLogin(id, null).GetHashCode());
        Assert.NotEqual(new RecentGoogleLogin(id, at), new RecentGoogleLogin(id, at.AddSeconds(1)));
        Assert.NotEqual(new RecentGoogleLogin(id, at), new RecentGoogleLogin(id, null));
        Assert.NotEqual(new RecentGoogleLogin(id, at), new RecentGoogleLogin(new PlayerId(Guid.NewGuid()), at));
        Assert.False(new RecentGoogleLogin(id, at).Equals(null));
    }

    [Fact]
    public void REQ_042_RecentGoogleLogin_equality_compares_the_instant_not_the_offset()
    {
        var id = new PlayerId(Guid.NewGuid());
        var utc = new RecentGoogleLogin(id, Now);
        var shifted = new RecentGoogleLogin(id, Now.ToOffset(TimeSpan.FromHours(-3)));

        Assert.Equal(utc, shifted);
        Assert.Equal(utc.GetHashCode(), shifted.GetHashCode());
    }

    [Fact]
    public void REQ_026_the_two_proof_kinds_are_never_equal_even_for_the_same_player()
    {
        var id = new PlayerId(Guid.NewGuid());
        object proof = new PasswordProof(id);
        object login = new RecentGoogleLogin(id, Now);

        Assert.False(proof.Equals(login));
        Assert.False(login.Equals(proof));
    }

    private sealed record PlayerState(
        Email Email,
        PasswordHash? PasswordHash,
        ExternalIdentity? ExternalIdentity,
        DisplayName DisplayName,
        Avatar Avatar,
        Bio? Bio,
        FavoriteTcg FavoriteTcg,
        FormatPreferences Formats,
        TermsAcceptance TermsAcceptance);
}
