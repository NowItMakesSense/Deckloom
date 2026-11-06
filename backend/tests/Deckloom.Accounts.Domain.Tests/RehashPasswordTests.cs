using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-033 (REQ-038, ADR-0003): migracao transparente do hash apos login bem-sucedido.
// Assinatura: Player.RehashPassword(PasswordHash newHash) : Result<Player>.
// Escolha: Result de falha (nao excecao), como IssuePasswordReset e LinkGoogle tratam "sem senha local":
// DomainError(Player.NoLocalPasswordCode) sem Field. Nao emite evento (nao e troca pelo usuario, nao e
// PasswordChanged) e nao toca em mais nada do agregado. Player nao tem estado "excluido" (Delete so emite
// PlayerDeleted), entao nao ha regra de conta excluida a testar aqui.
public class RehashPasswordTests
{
    private const string Current = "2026-10-01";
    private const string OldHash =
        "v1$pbkdf2-sha256$100000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";
    private const string NewHash =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static Player APasswordPlayer()
    {
        var player = Player.RegisterWithPassword(
            Email.Create("ana@example.com").Value,
            PasswordHash.Parse(OldHash).Value,
            DisplayName.Create("Ana").Value,
            Current, Current, true, true, Now, "preset-01").Value;
        player.ClearDomainEvents();
        return player;
    }

    private static Player AGoogleOnlyPlayer()
    {
        var player = Player.RegisterWithGoogle(
            Email.Create("ana@example.com").Value,
            ExternalIdentity.Create("google", "sub-1").Value,
            DisplayName.Create("Ana").Value,
            Current, Current, true, true, Now, "preset-01").Value;
        player.ClearDomainEvents();
        return player;
    }

    [Fact]
    public void REQ_038_replaces_the_stored_hash_with_the_new_one()
    {
        var player = APasswordPlayer();
        var newHash = PasswordHash.Parse(NewHash).Value;

        var result = player.RehashPassword(newHash);

        Assert.True(result.IsSuccess);
        Assert.Same(player, result.Value);
        Assert.Equal(NewHash, player.PasswordHash!.ToStoredString());
    }

    [Fact]
    public void REQ_038_keeps_the_rest_of_the_aggregate_unchanged()
    {
        var player = APasswordPlayer();
        var id = player.Id;
        var email = player.Email;
        var name = player.DisplayName;
        var avatar = player.Avatar;
        var terms = player.TermsAcceptance;

        player.RehashPassword(PasswordHash.Parse(NewHash).Value);

        Assert.Equal(id, player.Id);
        Assert.Equal(email, player.Email);
        Assert.Equal(name, player.DisplayName);
        Assert.Equal(avatar, player.Avatar);
        Assert.Equal(terms, player.TermsAcceptance);
        Assert.Null(player.ExternalIdentity);
        Assert.Null(player.PasswordResetToken);
    }

    [Fact]
    public void REQ_038_does_not_raise_any_event_in_particular_not_password_changed()
    {
        var player = APasswordPlayer();

        player.RehashPassword(PasswordHash.Parse(NewHash).Value);

        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_038_does_not_discard_a_pending_password_reset_token()
    {
        var player = APasswordPlayer();
        player.IssuePasswordReset(TokenHash.Create(new byte[TokenHash.Length]).Value, Now);
        player.ClearDomainEvents();

        player.RehashPassword(PasswordHash.Parse(NewHash).Value);

        Assert.NotNull(player.PasswordResetToken);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_038_a_google_only_player_has_no_local_password_to_rehash()
    {
        var player = AGoogleOnlyPlayer();

        var result = player.RehashPassword(PasswordHash.Parse(NewHash).Value);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(Player.NoLocalPasswordCode, error.Code);
        Assert.Null(error.Field);
        Assert.Null(player.PasswordHash);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_038_a_null_hash_is_rejected()
    {
        var player = APasswordPlayer();

        Assert.Throws<ArgumentNullException>(() => player.RehashPassword(null!));
    }
}
