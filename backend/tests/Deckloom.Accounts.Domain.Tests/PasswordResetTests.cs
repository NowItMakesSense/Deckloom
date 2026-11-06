using System.Reflection;
using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-019 (REQ-029, REQ-030, REQ-031, REQ-038; J8): redefinicao de senha no agregado Player.
// Design (emenda 2026-10-23, "Redefinicao de senha"; vence a assinatura antiga `ResetPassword(newHash, now)` do tasks.md):
//   IssuePasswordReset(TokenHash, now) : Result<Player> - substitui o token anterior (no maximo um),
//     IssuedAt = now, ExpiresAt = now + 60 min, evento PasswordResetIssued(PlayerId, ExpiresAt);
//     conta sem senha local -> no_local_password.
//   ResetPassword(presented, newHash, now) : Result<Player> - aceita enquanto now < ExpiresAt (59:59 ok; 60:00 e 60:01 nao);
//     sem token, usado, expirado ou diferente -> reset_link_invalid; sucesso troca o hash, remove o token, emite PasswordChanged(PlayerId).
//   Falhas: Field nulo, sem mudanca de estado e sem evento. Leitura: Player.PasswordResetToken? (TokenHash, IssuedAt, ExpiresAt).
// O tempo entra como DateTimeOffset (a API do agregado recebe `now`); o pacote FakeTimeProvider nao e referenciado em Domain.Tests.
public class PasswordResetTests
{
    private const string Current = "2026-10-01";
    private const string OldStored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";
    private const string NewStored =
        "v1$pbkdf2-sha256$600000$b3V0cm9zYWx0b3V0cm9z$bm92b2hhc2hub3ZvaGFzaG5vdm9oYXNobm92b2hhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(60);

    private static PasswordHash NewHash => PasswordHash.Parse(NewStored).Value;

    private static TokenHash Token(byte seed = 1) =>
        TokenHash.Create(Enumerable.Range(0, 32).Select(i => (byte)(seed + i)).ToArray()).Value;

    private static Player APasswordPlayer()
    {
        var player = Player.RegisterWithPassword(
            Email.Create("ana@example.com").Value,
            PasswordHash.Parse(OldStored).Value,
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
            Email.Create("ana@example.com").Value,
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

    private static Player AnIssuedPlayer(byte seed = 1)
    {
        var player = APasswordPlayer();
        Assert.True(player.IssuePasswordReset(Token(seed), Now).IsSuccess);
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

    // IssuePasswordReset

    [Fact]
    public void REQ_029_a_new_player_has_no_reset_token()
    {
        Assert.Null(APasswordPlayer().PasswordResetToken);
    }

    [Fact]
    public void REQ_029_issuing_stores_the_token_hash_with_issued_at_now_and_expiry_in_60_minutes()
    {
        var player = APasswordPlayer();
        var hash = Token();

        var result = player.IssuePasswordReset(hash, Now);

        Assert.True(result.IsSuccess);
        Assert.Same(player, result.Value);
        var token = Assert.IsType<PasswordResetToken>(player.PasswordResetToken);
        Assert.Equal(hash, token.TokenHash);
        Assert.Equal(Now, token.IssuedAt);
        Assert.Equal(Now + Window, token.ExpiresAt);
    }

    [Fact]
    public void REQ_029_issuing_emits_PasswordResetIssued_with_the_player_id_and_expiry()
    {
        var player = APasswordPlayer();

        player.IssuePasswordReset(Token(), Now);

        var issued = Assert.IsType<PasswordResetIssued>(Assert.Single(player.DomainEvents));
        Assert.Equal(player.Id, issued.PlayerId);
        Assert.Equal(Now + Window, issued.ExpiresAt);
    }

    [Fact]
    public void REQ_029_issuing_does_not_change_the_current_password()
    {
        var player = APasswordPlayer();
        var hash = player.PasswordHash;

        player.IssuePasswordReset(Token(), Now);

        Assert.Same(hash, player.PasswordHash);
    }

    [Fact]
    public void REQ_029_a_second_issue_replaces_the_first_token_so_only_one_exists()
    {
        var player = AnIssuedPlayer(1);
        var later = Now.AddMinutes(10);

        var result = player.IssuePasswordReset(Token(2), later);

        Assert.True(result.IsSuccess);
        var token = Assert.IsType<PasswordResetToken>(player.PasswordResetToken);
        Assert.Equal(Token(2), token.TokenHash);
        Assert.Equal(later, token.IssuedAt);
        Assert.Equal(later + Window, token.ExpiresAt);
        var issued = Assert.IsType<PasswordResetIssued>(Assert.Single(player.DomainEvents));
        Assert.Equal(later + Window, issued.ExpiresAt);
    }

    [Fact]
    public void REQ_029_after_a_second_issue_the_old_token_hash_is_reset_link_invalid_and_the_new_one_works()
    {
        var player = AnIssuedPlayer(1);
        player.IssuePasswordReset(Token(2), Now.AddMinutes(1));
        player.ClearDomainEvents();
        var hashBefore = player.PasswordHash;

        var old = player.ResetPassword(Token(1), NewHash, Now.AddMinutes(2));

        AssertSingleError(old, "reset_link_invalid");
        Assert.Same(hashBefore, player.PasswordHash);
        Assert.Equal(Token(2), player.PasswordResetToken!.TokenHash);
        Assert.Empty(player.DomainEvents);

        var fresh = player.ResetPassword(Token(2), NewHash, Now.AddMinutes(2));

        Assert.True(fresh.IsSuccess);
    }

    [Fact]
    public void REQ_029_issuing_again_with_the_same_hash_restarts_the_window()
    {
        var player = AnIssuedPlayer(1);
        var later = Now.AddMinutes(30);

        player.IssuePasswordReset(Token(1), later);

        Assert.Equal(later + Window, player.PasswordResetToken!.ExpiresAt);
        Assert.True(player.ResetPassword(Token(1), NewHash, Now.AddMinutes(89)).IsSuccess);
    }

    [Fact]
    public void REQ_029_a_google_only_player_cannot_issue_and_gets_no_local_password()
    {
        var player = AGoogleOnlyPlayer();

        var result = player.IssuePasswordReset(Token(), Now);

        AssertSingleError(result, "no_local_password");
        Assert.Equal(Player.NoLocalPasswordCode, result.Error.Code);
        Assert.Null(player.PasswordResetToken);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_029_a_null_token_hash_on_issue_throws_and_changes_nothing()
    {
        var player = AnIssuedPlayer();
        var before = player.PasswordResetToken;

        Assert.Throws<ArgumentNullException>(() => player.IssuePasswordReset(null!, Now));

        Assert.Same(before, player.PasswordResetToken);
        Assert.Empty(player.DomainEvents);
    }

    // ResetPassword - sucesso

    [Fact]
    public void REQ_030_the_presented_token_within_the_window_changes_the_password_hash()
    {
        var player = AnIssuedPlayer();
        var newHash = NewHash;

        var result = player.ResetPassword(Token(), newHash, Now.AddMinutes(10));

        Assert.True(result.IsSuccess);
        Assert.Same(player, result.Value);
        Assert.Same(newHash, player.PasswordHash);
    }

    [Fact]
    public void REQ_031_success_consumes_the_token_and_emits_PasswordChanged_with_only_the_player_id()
    {
        var player = AnIssuedPlayer();

        player.ResetPassword(Token(), NewHash, Now.AddMinutes(10));

        Assert.Null(player.PasswordResetToken);
        var changed = Assert.IsType<PasswordChanged>(Assert.Single(player.DomainEvents));
        Assert.Equal(player.Id, changed.PlayerId);
    }

    [Fact]
    public void REQ_030_a_successful_reset_cannot_be_repeated_with_the_same_token()
    {
        var player = AnIssuedPlayer();
        var firstNew = NewHash;
        player.ResetPassword(Token(), firstNew, Now.AddMinutes(10));
        player.ClearDomainEvents();
        var otherNew = PasswordHash.Parse(OldStored).Value;

        var again = player.ResetPassword(Token(), otherNew, Now.AddMinutes(11));

        AssertSingleError(again, "reset_link_invalid");
        Assert.Same(firstNew, player.PasswordHash);
        Assert.Null(player.PasswordResetToken);
        Assert.Empty(player.DomainEvents);
    }

    // ResetPassword - janela

    [Fact]
    public void REQ_030_accepts_at_59_minutes_59_seconds()
    {
        var player = AnIssuedPlayer();

        var result = player.ResetPassword(Token(), NewHash, Now + Window - TimeSpan.FromSeconds(1));

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(3600)]
    [InlineData(3601)]
    [InlineData(86400)]
    public void REQ_030_rejects_at_60_minutes_and_after_as_reset_link_invalid_keeping_hash_and_token(int seconds)
    {
        var player = AnIssuedPlayer();
        var hashBefore = player.PasswordHash;
        var tokenBefore = player.PasswordResetToken;

        var result = player.ResetPassword(Token(), NewHash, Now.AddSeconds(seconds));

        AssertSingleError(result, "reset_link_invalid");
        Assert.Same(hashBefore, player.PasswordHash);
        Assert.Same(tokenBefore, player.PasswordResetToken);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_030_the_window_counts_from_the_latest_issue_not_from_the_first()
    {
        var player = AnIssuedPlayer(1);
        player.IssuePasswordReset(Token(2), Now.AddMinutes(50));

        var result = player.ResetPassword(Token(2), NewHash, Now.AddMinutes(100));

        Assert.True(result.IsSuccess);
    }

    // ResetPassword - rejeicoes

    [Fact]
    public void REQ_030_without_any_issued_token_it_is_reset_link_invalid()
    {
        var player = APasswordPlayer();
        var hashBefore = player.PasswordHash;

        var result = player.ResetPassword(Token(), NewHash, Now);

        AssertSingleError(result, "reset_link_invalid");
        Assert.Same(hashBefore, player.PasswordHash);
        Assert.Null(player.PasswordResetToken);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_030_a_different_token_hash_is_reset_link_invalid_and_keeps_the_active_token()
    {
        var player = AnIssuedPlayer(1);
        var hashBefore = player.PasswordHash;
        var tokenBefore = player.PasswordResetToken;

        var result = player.ResetPassword(Token(9), NewHash, Now.AddMinutes(1));

        AssertSingleError(result, "reset_link_invalid");
        Assert.Same(hashBefore, player.PasswordHash);
        Assert.Same(tokenBefore, player.PasswordResetToken);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_030_a_failed_attempt_does_not_burn_the_token_so_the_right_hash_still_works()
    {
        var player = AnIssuedPlayer(1);
        player.ResetPassword(Token(9), NewHash, Now.AddMinutes(1));

        var result = player.ResetPassword(Token(1), NewHash, Now.AddMinutes(2));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void REQ_030_a_google_only_player_has_no_token_to_reset_with()
    {
        var player = AGoogleOnlyPlayer();

        var result = player.ResetPassword(Token(), NewHash, Now);

        AssertSingleError(result, "reset_link_invalid");
        Assert.Null(player.PasswordHash);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_030_null_arguments_on_reset_throw_and_change_nothing()
    {
        var player = AnIssuedPlayer();
        var hashBefore = player.PasswordHash;
        var tokenBefore = player.PasswordResetToken;

        Assert.Throws<ArgumentNullException>(() => player.ResetPassword(null!, NewHash, Now));
        Assert.Throws<ArgumentNullException>(() => player.ResetPassword(Token(), null!, Now));

        Assert.Same(hashBefore, player.PasswordHash);
        Assert.Same(tokenBefore, player.PasswordResetToken);
        Assert.Empty(player.DomainEvents);
    }

    // REQ-038: so o hash do token e guardado; eventos so com identificadores e ExpiresAt

    [Fact]
    public void REQ_038_the_aggregate_exposes_the_token_only_as_a_hash_value_object()
    {
        var token = AnIssuedPlayer().PasswordResetToken!;

        var properties = typeof(PasswordResetToken)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (property.Name, property.PropertyType))
            .OrderBy(pair => pair.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                ("ExpiresAt", typeof(DateTimeOffset)),
                ("IssuedAt", typeof(DateTimeOffset)),
                ("TokenHash", typeof(TokenHash)),
            ],
            properties);
        Assert.Equal("TokenHash(***)", token.TokenHash.ToString());
    }

    [Fact]
    public void REQ_038_PasswordResetToken_is_sealed_read_only_with_an_internal_constructor()
    {
        var type = typeof(PasswordResetToken);

        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors());
        Assert.NotEmpty(type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.All(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void REQ_038_PasswordResetIssued_carries_only_the_player_id_and_the_expiry()
    {
        var properties = typeof(PasswordResetIssued)
            .GetProperties()
            .Where(property => property.DeclaringType == typeof(PasswordResetIssued))
            .Select(property => (property.Name, property.PropertyType))
            .ToArray();

        Assert.Equal([("PlayerId", typeof(PlayerId)), ("ExpiresAt", typeof(DateTimeOffset))], properties);
    }

    [Fact]
    public void REQ_038_PasswordChanged_carries_only_the_player_id()
    {
        var properties = typeof(PasswordChanged)
            .GetProperties()
            .Where(property => property.DeclaringType == typeof(PasswordChanged))
            .Select(property => (property.Name, property.PropertyType))
            .ToArray();

        Assert.Equal([("PlayerId", typeof(PlayerId))], properties);
    }
}
