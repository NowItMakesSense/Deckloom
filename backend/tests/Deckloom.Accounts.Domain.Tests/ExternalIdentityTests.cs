using System.Reflection;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// T-014 (REQ-006, REQ-007): value object ExternalIdentity.
// Design (tabela de invariantes): "`Provider = google`, `Subject` nao vazio" (REQ-006, 007).
// Task T-014: "igualdade por (provedor, subject)".
// Contrato assumido para o GREEN (ver relatorio):
//   ExternalIdentity.Create(string? provider, string? subject) : Result<ExternalIdentity>
//   .Provider (string, "google"), .Subject (string, guardado exatamente como recebido, sem trim)
//   Falhas sem Field (nao mapeiam para campo da API): provider != "google" (ordinal) -> "invalid";
//   subject nulo/vazio/so espacos -> "required".
public class ExternalIdentityTests
{
    [Fact]
    public void REQ_006_google_provider_with_subject_is_ok()
    {
        var result = ExternalIdentity.Create("google", "110169484474386276334");

        Assert.True(result.IsSuccess);
        Assert.Equal("google", result.Value.Provider);
        Assert.Equal("110169484474386276334", result.Value.Subject);
    }

    [Theory]
    [InlineData("facebook")]
    [InlineData("Google")]
    [InlineData("GOOGLE")]
    [InlineData(" google")]
    [InlineData("")]
    [InlineData(null)]
    public void REQ_006_provider_other_than_google_is_rejected(string? provider)
    {
        var result = ExternalIdentity.Create(provider, "sub-1");

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
        Assert.Null(result.Error.Field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void REQ_006_empty_subject_is_rejected(string? subject)
    {
        var result = ExternalIdentity.Create("google", subject);

        Assert.True(result.IsFailure);
        Assert.Equal("required", result.Error.Code);
        Assert.Null(result.Error.Field);
    }

    [Fact]
    public void REQ_007_subject_is_kept_exactly_as_received()
    {
        var result = ExternalIdentity.Create("google", "  Sub-ABC  ");

        Assert.Equal("  Sub-ABC  ", result.Value.Subject);
    }

    [Fact]
    public void REQ_007_equality_is_by_provider_and_subject()
    {
        var first = ExternalIdentity.Create("google", "sub-1").Value;
        var same = ExternalIdentity.Create("google", "sub-1").Value;
        var other = ExternalIdentity.Create("google", "sub-2").Value;

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void REQ_007_subject_comparison_is_case_sensitive()
    {
        var lower = ExternalIdentity.Create("google", "abc").Value;
        var upper = ExternalIdentity.Create("google", "ABC").Value;

        Assert.NotEqual(lower, upper);
    }

    [Fact]
    public void REQ_006_is_immutable_and_only_built_by_the_factory()
    {
        var properties = typeof(ExternalIdentity).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(properties);
        Assert.All(properties, property => Assert.Null(property.SetMethod));
        Assert.Empty(typeof(ExternalIdentity).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }
}
