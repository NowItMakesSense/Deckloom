using System.Reflection;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-028 (habilitadora; REQ-006, REQ-022): IGoogleIdentityVerifier
//   Task<Uri> BuildAuthorizationUrlAsync(string state, string nonce, string codeChallenge, ct)
//   Task<GoogleVerifiedIdentity?> CompleteAsync(string code, string codeVerifier, string nonce, ct)
//     nulo = Jogador cancelou/negou no Google; GoogleVerificationFailedException = falha do provedor/validacao.
// GoogleVerifiedIdentity(string Subject, Email Email, bool EmailVerified, string? Name): sem foto nem tokens (REQ-022).
// Fake programavel: Returns(identity), Cancel(), Fail(); registra CompleteCalls (Code, CodeVerifier, Nonce).
public class FakeGoogleIdentityVerifierTests
{
    private readonly FakeGoogleIdentityVerifier _verifier = new();

    private static GoogleVerifiedIdentity AnIdentity(string subject = "sub-1") =>
        new(subject, AnEmail("gui@example.com"), true, "Gui Google");

    [Fact]
    public void REQ022_GoogleVerifiedIdentity_carries_only_subject_email_verification_name_and_auth_time()
    {
        var properties = typeof(GoogleVerifiedIdentity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Order();

        Assert.Equal(["AuthTime", "Email", "EmailVerified", "Name", "Subject"], properties);
    }

    [Fact]
    public void REQ022_GoogleVerifiedIdentity_has_value_equality()
    {
        Assert.Equal(AnIdentity(), AnIdentity());
        Assert.NotEqual(AnIdentity("a"), AnIdentity("b"));
    }

    [Fact]
    public async Task REQ006_Returns_makes_CompleteAsync_return_the_programmed_identity()
    {
        var identity = AnIdentity();
        _verifier.Returns(identity);

        var result = await _verifier.CompleteAsync("code-1", "verifier-1", "nonce-1");

        Assert.Equal(identity, result);
    }

    [Fact]
    public async Task REQ006_Records_the_code_verifier_and_nonce_received()
    {
        _verifier.Returns(AnIdentity());

        await _verifier.CompleteAsync("code-1", "verifier-1", "nonce-1");

        var call = Assert.Single(_verifier.CompleteCalls);
        Assert.Equal(("code-1", "verifier-1", "nonce-1"), (call.Code, call.CodeVerifier, call.Nonce));
    }

    [Fact]
    public async Task REQ006_Cancel_makes_CompleteAsync_return_null()
    {
        _verifier.Cancel();

        Assert.Null(await _verifier.CompleteAsync("code", "verifier", "nonce"));
    }

    [Fact]
    public async Task REQ006_Fail_makes_CompleteAsync_throw_GoogleVerificationFailedException()
    {
        _verifier.Fail();

        await Assert.ThrowsAsync<GoogleVerificationFailedException>(() =>
            _verifier.CompleteAsync("code", "verifier", "nonce"));
    }

    [Fact]
    public async Task REQ006_Unprogrammed_CompleteAsync_fails_loudly_instead_of_inventing_an_identity()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _verifier.CompleteAsync("code", "verifier", "nonce"));
    }

    [Fact]
    public async Task REQ006_The_last_programming_wins()
    {
        _verifier.Fail();
        _verifier.Returns(AnIdentity());

        Assert.NotNull(await _verifier.CompleteAsync("code", "verifier", "nonce"));
    }

    [Fact]
    public async Task REQ006_BuildAuthorizationUrl_returns_an_absolute_url_carrying_state_nonce_and_challenge()
    {
        var url = await _verifier.BuildAuthorizationUrlAsync("st-1", "no-1", "ch-1");

        Assert.True(url.IsAbsoluteUri);
        Assert.Contains("state=st-1", url.Query);
        Assert.Contains("nonce=no-1", url.Query);
        Assert.Contains("code_challenge=ch-1", url.Query);
    }
}
