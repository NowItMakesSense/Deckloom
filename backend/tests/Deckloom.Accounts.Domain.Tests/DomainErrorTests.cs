using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-013 (habilitadora): DomainError ganha `Field` opcional: DomainError(string Code, string? Field = null).
// Usos existentes `new DomainError("x")` continuam compilando.
public class DomainErrorTests
{
    [Fact]
    public void Field_is_kept_when_given()
    {
        var error = new DomainError("required", "acceptTerms");

        Assert.Equal("required", error.Code);
        Assert.Equal("acceptTerms", error.Field);
    }

    [Fact]
    public void Field_is_null_when_omitted()
    {
        var error = new DomainError("required");

        Assert.Equal("required", error.Code);
        Assert.Null(error.Field);
    }

    [Fact]
    public void Equality_includes_the_field()
    {
        Assert.Equal(new DomainError("required", "acceptTerms"), new DomainError("required", "acceptTerms"));
        Assert.NotEqual(new DomainError("required", "acceptTerms"), new DomainError("required", "adultDeclaration"));
        Assert.NotEqual(new DomainError("required", "acceptTerms"), new DomainError("required"));
    }
}
