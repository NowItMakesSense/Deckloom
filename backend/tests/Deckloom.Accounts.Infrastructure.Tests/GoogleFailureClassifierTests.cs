using System.Text.Json;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Infrastructure;

namespace Deckloom.Accounts.Infrastructure.Tests;

// QA-05 (fase 6): testes de REGRESSAO/caracterizacao do mapeamento de falhas do fluxo OIDC (evento 1010,
// design.md) e dos nomes snake_case do catalogo. Nao ha RED: o comportamento ja existia e so era exercitado
// de forma indireta (ramos de erro sem teste). Cada caso fixa um ramo de GoogleFailureClassifier /
// GoogleAuthFailureLogNames; a prova de que pegam regressao esta no relatorio da fase 6 (mutacao dos ramos).
public sealed class GoogleFailureClassifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static GoogleVerifiedIdentity Identity(bool emailVerified = true, DateTimeOffset? authTime = null) =>
        new("sub-1", Email.Create("jogadora@example.com").Value, emailVerified, "Jogadora", authTime);

    [Fact]
    public void Cancellation_requested_by_the_caller_is_not_classified()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Null(GoogleFailureClassifier.ClassifyTransport(new OperationCanceledException(), cts.Token));
    }

    [Fact]
    public void Cancellation_not_requested_by_the_caller_is_a_timeout_at_the_token_stage()
    {
        var failure = GoogleFailureClassifier.ClassifyTransport(new TaskCanceledException(), CancellationToken.None);

        Assert.Equal(new GoogleAuthFailed(GoogleAuthFailureStage.Token, GoogleAuthFailureReason.Timeout), failure);
    }

    public static TheoryData<Exception> TransportFailures() => new()
    {
        new HttpRequestException("falha"),
        new JsonException("json"),
        new InvalidOperationException("op"),
        new KeyNotFoundException("chave"),
    };

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public void Transport_failures_are_provider_errors_at_the_token_stage(Exception exception)
    {
        var failure = GoogleFailureClassifier.ClassifyTransport(exception, CancellationToken.None);

        Assert.Equal(new GoogleAuthFailed(GoogleAuthFailureStage.Token, GoogleAuthFailureReason.ProviderError), failure);
    }

    [Fact]
    public void An_unexpected_exception_is_not_classified()
    {
        Assert.Null(GoogleFailureClassifier.ClassifyTransport(new ArgumentException("bug"), CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"error\":\"access_denied\"}", GoogleAuthFailureReason.Cancelled)]
    [InlineData("{\"error\":\"invalid_grant\"}", GoogleAuthFailureReason.ProviderError)]
    [InlineData("{\"error\":42}", GoogleAuthFailureReason.ProviderError)]
    [InlineData("{\"outro\":\"access_denied\"}", GoogleAuthFailureReason.ProviderError)]
    [InlineData("isto nao e json", GoogleAuthFailureReason.ProviderError)]
    public void A_token_error_body_maps_access_denied_to_cancelled_and_everything_else_to_provider_error(
        string body, GoogleAuthFailureReason expected)
    {
        var failure = GoogleFailureClassifier.ClassifyTokenError(body);

        Assert.Equal(new GoogleAuthFailed(GoogleAuthFailureStage.Token, expected), failure);
    }

    [Theory]
    [InlineData("{\"id_token\":\"abc.def.ghi\"}", "abc.def.ghi")]
    [InlineData("{\"id_token\":null}", null)]
    [InlineData("{\"access_token\":\"x\"}", null)]
    [InlineData("isto nao e json", null)]
    public void The_id_token_is_extracted_only_from_a_well_formed_token_response(string body, string? expected)
    {
        Assert.Equal(expected, GoogleFailureClassifier.ExtractIdToken(body));
    }

    [Fact]
    public void An_unverified_email_fails_at_the_id_token_stage_for_any_intent()
    {
        var failure = GoogleFailureClassifier.ClassifyIdentity(
            Identity(emailVerified: false), GoogleAuthorizationIntent.SignIn, Now, 300);

        Assert.Equal(new GoogleAuthFailed(GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.UnverifiedEmail), failure);
    }

    [Fact]
    public void A_sign_in_ignores_a_stale_or_missing_auth_time()
    {
        Assert.Null(GoogleFailureClassifier.ClassifyIdentity(
            Identity(authTime: null), GoogleAuthorizationIntent.SignIn, Now, 300));
        Assert.Null(GoogleFailureClassifier.ClassifyIdentity(
            Identity(authTime: Now.AddHours(-2)), GoogleAuthorizationIntent.SignIn, Now, 300));
    }

    [Fact]
    public void A_reauth_without_auth_time_fails_as_provider_error()
    {
        var failure = GoogleFailureClassifier.ClassifyIdentity(
            Identity(authTime: null), GoogleAuthorizationIntent.Reauth, Now, 300);

        Assert.Equal(new GoogleAuthFailed(GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.ProviderError), failure);
    }

    [Fact]
    public void A_reauth_older_than_the_window_fails_but_exactly_at_the_window_is_accepted()
    {
        Assert.Null(GoogleFailureClassifier.ClassifyIdentity(
            Identity(authTime: Now.AddSeconds(-300)), GoogleAuthorizationIntent.Reauth, Now, 300));

        var failure = GoogleFailureClassifier.ClassifyIdentity(
            Identity(authTime: Now.AddSeconds(-301)), GoogleAuthorizationIntent.Reauth, Now, 300);

        Assert.Equal(new GoogleAuthFailed(GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.ProviderError), failure);
    }

    [Theory]
    [InlineData(GoogleAuthFailureStage.Authorize, "authorize")]
    [InlineData(GoogleAuthFailureStage.Callback, "callback")]
    [InlineData(GoogleAuthFailureStage.Token, "token")]
    [InlineData(GoogleAuthFailureStage.IdToken, "id_token")]
    public void Stage_names_follow_the_event_1010_catalog(GoogleAuthFailureStage stage, string expected)
    {
        Assert.Equal(expected, GoogleAuthFailureLogNames.For(stage));
    }

    [Theory]
    [InlineData(GoogleAuthFailureReason.Cancelled, "cancelled")]
    [InlineData(GoogleAuthFailureReason.StateMismatch, "state_mismatch")]
    [InlineData(GoogleAuthFailureReason.UnverifiedEmail, "unverified_email")]
    [InlineData(GoogleAuthFailureReason.ProviderError, "provider_error")]
    [InlineData(GoogleAuthFailureReason.Timeout, "timeout")]
    [InlineData(GoogleAuthFailureReason.SubjectMismatch, "subject_mismatch")]
    public void Reason_names_follow_the_event_1010_catalog(GoogleAuthFailureReason reason, string expected)
    {
        Assert.Equal(expected, GoogleAuthFailureLogNames.For(reason));
    }

    [Fact]
    public void Names_for_values_outside_the_catalog_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GoogleAuthFailureLogNames.For((GoogleAuthFailureStage)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoogleAuthFailureLogNames.For((GoogleAuthFailureReason)99));
    }
}
