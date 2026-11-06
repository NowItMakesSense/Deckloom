using System.Reflection;
using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-013 (REQ-033, REQ-034, REQ-043, REQ-048): value object TermsAcceptance.
// Design (tabela de invariantes): "`TermsVersion` igual a versao vigente (`ITermsVersionProvider`), `AcceptedAt`,
// `AdultDeclared = true`; imutavel; removido com o Jogador". J4: "`TermsAcceptance` (versao vigente + data/hora + 18+)
// obrigatorio na criacao, por qualquer via; versao divergente recusa a criacao".
// Codigos de campo do contrato: `required`, `version_mismatch`. Erro = DomainError(Code, Field) com Field do contrato.
// Decisao: devolve o PRIMEIRO erro na ordem fixa: acceptTerms/required -> acceptTerms/version_mismatch ->
// adultDeclaration/required.
// Contrato assumido para o GREEN (ver relatorio):
//   TermsAcceptance.Create(string currentVersion, string? submittedVersion, bool adultDeclared, bool accepted,
//                          DateTimeOffset now) : Result<TermsAcceptance>
//   .TermsVersion (string, = versao vigente), .AcceptedAt (DateTimeOffset, = now), .AdultDeclared (bool)
//   Comparacao de versao ordinal exata (sem trim, sensivel a maiusculas).
public class TermsAcceptanceTests
{
    private const string Current = "2026-10-01";
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void REQ_033_current_version_with_acceptance_and_adult_declaration_is_ok()
    {
        var result = TermsAcceptance.Create(Current, Current, adultDeclared: true, accepted: true, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Current, result.Value.TermsVersion);
        Assert.Equal(Now, result.Value.AcceptedAt);
    }

    [Fact]
    public void REQ_048_adult_declaration_is_kept_as_evidence()
    {
        var result = TermsAcceptance.Create(Current, Current, adultDeclared: true, accepted: true, Now);

        Assert.True(result.Value.AdultDeclared);
    }

    [Fact]
    public void REQ_033_without_acceptance_is_rejected_with_required()
    {
        var result = TermsAcceptance.Create(Current, Current, adultDeclared: true, accepted: false, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("required", result.Error.Code);
        Assert.Equal("acceptTerms", result.Error.Field);
    }

    [Fact]
    public void REQ_034_without_adult_declaration_is_rejected_with_required()
    {
        var result = TermsAcceptance.Create(Current, Current, adultDeclared: false, accepted: true, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("required", result.Error.Code);
        Assert.Equal("adultDeclaration", result.Error.Field);
    }

    [Theory]
    [InlineData("2026-09-01")]
    [InlineData("2026-10-01 ")]
    [InlineData("")]
    [InlineData(null)]
    public void REQ_043_version_different_from_current_is_rejected_with_version_mismatch(string? submitted)
    {
        var result = TermsAcceptance.Create(Current, submitted, adultDeclared: true, accepted: true, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("version_mismatch", result.Error.Code);
        Assert.Equal("acceptTerms", result.Error.Field);
    }

    [Fact]
    public void REQ_043_version_comparison_is_case_sensitive()
    {
        var result = TermsAcceptance.Create("v1-A", "v1-a", adultDeclared: true, accepted: true, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("version_mismatch", result.Error.Code);
        Assert.Equal("acceptTerms", result.Error.Field);
    }

    [Fact]
    public void REQ_043_not_accepted_with_wrong_version_reports_acceptTerms_required_first()
    {
        var result = TermsAcceptance.Create(Current, "2026-09-01", adultDeclared: true, accepted: false, Now);

        Assert.Equal(new DomainError("required", "acceptTerms"), result.Error);
    }

    [Fact]
    public void REQ_034_not_accepted_and_not_adult_reports_acceptTerms_required_first()
    {
        var result = TermsAcceptance.Create(Current, Current, adultDeclared: false, accepted: false, Now);

        Assert.Equal(new DomainError("required", "acceptTerms"), result.Error);
    }

    [Fact]
    public void REQ_043_wrong_version_and_not_adult_reports_version_mismatch_first()
    {
        var result = TermsAcceptance.Create(Current, "2026-09-01", adultDeclared: false, accepted: true, Now);

        Assert.Equal(new DomainError("version_mismatch", "acceptTerms"), result.Error);
    }

    [Fact]
    public void REQ_033_equality_is_by_value()
    {
        var first = TermsAcceptance.Create(Current, Current, true, true, Now).Value;
        var second = TermsAcceptance.Create(Current, Current, true, true, Now).Value;
        var later = TermsAcceptance.Create(Current, Current, true, true, Now.AddMinutes(1)).Value;

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, later);
    }

    [Fact]
    public void REQ_033_is_immutable_without_public_setters()
    {
        var properties = typeof(TermsAcceptance).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(properties);
        Assert.All(properties, property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void REQ_033_cannot_be_constructed_outside_the_factory()
    {
        Assert.Empty(typeof(TermsAcceptance).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    // R-08: versao vigente nula ou em branco e erro de configuracao, nao DomainError

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void REQ_033_blank_current_version_is_a_configuration_error(string? currentVersion)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            TermsAcceptance.Create(currentVersion!, currentVersion, adultDeclared: true, accepted: true, Now));
    }

    [Fact]
    public void REQ_033_blank_current_version_is_a_configuration_error_even_when_the_submitted_version_differs()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            TermsAcceptance.Create(" ", Current, adultDeclared: true, accepted: true, Now));
    }
}
