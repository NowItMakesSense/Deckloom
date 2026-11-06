using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-018 (REQ-007, REQ-008, REQ-036; J7): vinculo Google a um Jogador existente.
// Design (emenda 2026-10-23, "Vinculo Google"): LinkGoogle(identity, verifiedEmail, proof?) : Result<Player>.
// Ordem fixa de erros, so o primeiro: no_local_password -> already_linked (mesmo subject inclusive)
// -> email_mismatch -> invalid_proof (prova nula ou de outro Jogador). Field nulo; falha nao vincula nem emite evento.
// Sucesso: ExternalIdentity = identidade informada e GoogleIdentityLinked(PlayerId).
public class LinkGoogleTests
{
    private const string Current = "2026-10-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static Email AnaEmail => Email.Create("ana@example.com").Value;

    private static ExternalIdentity Google(string subject = "google-subject-1") =>
        ExternalIdentity.Create("google", subject).Value;

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

    private static Player AGoogleOnlyPlayer()
    {
        var player = Player.RegisterWithGoogle(
            AnaEmail,
            Google(),
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

    private static PasswordProof ProofFor(Player player) => new(player.Id);

    private static Player ALinkedPlayer(string subject = "google-subject-1")
    {
        var player = APasswordPlayer();
        Assert.True(player.LinkGoogle(Google(subject), AnaEmail, ProofFor(player)).IsSuccess);
        player.ClearDomainEvents();
        return player;
    }

    private static void AssertSingleError(Result<Player> result, string code)
    {
        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Null(error.Field);
    }

    [Fact]
    public void REQ_007_a_password_player_with_matching_email_and_valid_proof_links_the_identity()
    {
        var player = APasswordPlayer();
        var identity = Google();

        var result = player.LinkGoogle(identity, AnaEmail, ProofFor(player));

        Assert.True(result.IsSuccess);
        Assert.Same(player, result.Value);
        Assert.Equal(identity, player.ExternalIdentity);
    }

    [Fact]
    public void REQ_036_linking_emits_GoogleIdentityLinked_with_the_player_id()
    {
        var player = APasswordPlayer();

        player.LinkGoogle(Google(), AnaEmail, ProofFor(player));

        var linked = Assert.IsType<GoogleIdentityLinked>(Assert.Single(player.DomainEvents));
        Assert.Equal(player.Id, linked.PlayerId);
    }

    [Fact]
    public void REQ_036_linking_keeps_the_local_password_and_the_email()
    {
        var player = APasswordPlayer();
        var hash = player.PasswordHash;

        player.LinkGoogle(Google(), AnaEmail, ProofFor(player));

        Assert.Equal(hash, player.PasswordHash);
        Assert.Equal(AnaEmail, player.Email);
    }

    [Fact]
    public void REQ_008_the_verified_email_is_compared_by_value_so_case_differences_still_match()
    {
        var player = APasswordPlayer();

        var result = player.LinkGoogle(Google(), Email.Create("  ANA@Example.com ").Value, ProofFor(player));

        Assert.True(result.IsSuccess);
        Assert.NotNull(player.ExternalIdentity);
    }

    [Fact]
    public void REQ_007_a_missing_proof_is_invalid_proof_and_links_nothing()
    {
        var player = APasswordPlayer();

        var result = player.LinkGoogle(Google(), AnaEmail, null);

        AssertSingleError(result, "invalid_proof");
        Assert.Null(player.ExternalIdentity);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_007_a_proof_of_another_player_is_invalid_proof_and_links_nothing()
    {
        var player = APasswordPlayer();
        var other = APasswordPlayer("bia@example.com");

        var result = player.LinkGoogle(Google(), AnaEmail, ProofFor(other));

        AssertSingleError(result, "invalid_proof");
        Assert.Null(player.ExternalIdentity);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_008_a_different_verified_email_is_email_mismatch_and_links_nothing()
    {
        var player = APasswordPlayer();

        var result = player.LinkGoogle(Google(), Email.Create("outra@example.com").Value, ProofFor(player));

        AssertSingleError(result, "email_mismatch");
        Assert.Null(player.ExternalIdentity);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_008_a_google_only_player_cannot_link_and_gets_no_local_password()
    {
        var player = AGoogleOnlyPlayer();
        var original = player.ExternalIdentity;

        var result = player.LinkGoogle(Google("google-subject-2"), AnaEmail, ProofFor(player));

        AssertSingleError(result, "no_local_password");
        Assert.Equal(original, player.ExternalIdentity);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_036_a_second_google_identity_is_already_linked_and_keeps_the_first()
    {
        var player = ALinkedPlayer("google-subject-1");

        var result = player.LinkGoogle(Google("google-subject-2"), AnaEmail, ProofFor(player));

        AssertSingleError(result, "already_linked");
        Assert.Equal(Google("google-subject-1"), player.ExternalIdentity);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_036_linking_the_same_subject_again_is_also_already_linked_without_event()
    {
        var player = ALinkedPlayer("google-subject-1");

        var result = player.LinkGoogle(Google("google-subject-1"), AnaEmail, ProofFor(player));

        AssertSingleError(result, "already_linked");
        Assert.Equal(Google("google-subject-1"), player.ExternalIdentity);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_008_no_local_password_wins_over_every_other_error()
    {
        var player = AGoogleOnlyPlayer();
        var other = APasswordPlayer("bia@example.com");

        var result = player.LinkGoogle(
            Google("google-subject-1"),
            Email.Create("outra@example.com").Value,
            ProofFor(other));

        AssertSingleError(result, "no_local_password");
    }

    [Fact]
    public void REQ_036_already_linked_wins_over_email_mismatch_and_invalid_proof()
    {
        var player = ALinkedPlayer();

        var result = player.LinkGoogle(Google("google-subject-2"), Email.Create("outra@example.com").Value, null);

        AssertSingleError(result, "already_linked");
    }

    [Fact]
    public void REQ_008_email_mismatch_wins_over_invalid_proof()
    {
        var player = APasswordPlayer();
        var other = APasswordPlayer("bia@example.com");

        var result = player.LinkGoogle(Google(), Email.Create("outra@example.com").Value, ProofFor(other));

        AssertSingleError(result, "email_mismatch");
    }

    [Fact]
    public void REQ_036_a_failed_link_attempt_can_be_retried_with_a_valid_proof()
    {
        var player = APasswordPlayer();
        player.LinkGoogle(Google(), AnaEmail, null);

        var result = player.LinkGoogle(Google(), AnaEmail, ProofFor(player));

        Assert.True(result.IsSuccess);
        Assert.Equal(Google(), player.ExternalIdentity);
        Assert.IsType<GoogleIdentityLinked>(Assert.Single(player.DomainEvents));
    }

    [Fact]
    public void REQ_036_PasswordProof_is_sealed_with_a_non_public_constructor_and_carries_only_the_player_id()
    {
        var type = typeof(PasswordProof);

        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
        Assert.NotEmpty(
            type.GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        Assert.Equal(PlayerIdOf(new PasswordProof(new PlayerId(Guid.Empty))), new PlayerId(Guid.Empty));
    }

    [Fact]
    public void REQ_036_the_event_carries_only_the_player_id()
    {
        var properties = typeof(GoogleIdentityLinked)
            .GetProperties()
            .Where(property => property.DeclaringType == typeof(GoogleIdentityLinked))
            .Select(property => (property.Name, property.PropertyType))
            .ToArray();

        Assert.Equal([("PlayerId", typeof(PlayerId))], properties);
    }

    private static PlayerId PlayerIdOf(PasswordProof proof) => proof.PlayerId;
}
