using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-001 (habilitadora, sem REQ direto): contrato do Result<T> e do DomainError do SharedKernel.
public class ResultTests
{
    [Fact]
    public void Success_exposes_the_value()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_exposes_a_domain_error_with_a_stable_code()
    {
        var error = new DomainError("invalid_format");

        var result = Result<int>.Failure(error);

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Equal("invalid_format", result.Error.Code);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Accessing_the_value_of_a_failure_is_an_error()
    {
        var result = Result<int>.Failure(new DomainError("required"));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Accessing_the_error_of_a_success_is_an_error()
    {
        var result = Result<int>.Success(1);

        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    // T-016 (emenda): Result<T> aceita varios erros; Error continua sendo o primeiro.
    [Fact]
    public void Failure_with_a_list_exposes_every_error_in_order()
    {
        var first = new DomainError("length", "displayName");
        var second = new DomainError("too_long", "bio");

        var result = Result<int>.Failure([first, second]);

        Assert.True(result.IsFailure);
        Assert.Equal([first, second], result.Errors);
    }

    [Fact]
    public void Error_of_a_failure_with_a_list_is_the_first_one()
    {
        var first = new DomainError("length", "displayName");

        var result = Result<int>.Failure([first, new DomainError("too_long", "bio")]);

        Assert.Equal(first, result.Error);
    }

    [Fact]
    public void Failure_with_a_single_error_exposes_it_in_errors()
    {
        var error = new DomainError("required");

        var result = Result<int>.Failure(error);

        Assert.Equal([error], result.Errors);
    }

    [Fact]
    public void Failure_with_an_empty_list_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Result<int>.Failure(new List<DomainError>()));
    }

    [Fact]
    public void Failure_with_a_null_list_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Result<int>.Failure((IReadOnlyList<DomainError>)null!));
    }

    [Fact]
    public void Accessing_the_errors_of_a_success_is_an_error()
    {
        var result = Result<int>.Success(1);

        Assert.Throws<InvalidOperationException>(() => result.Errors);
    }
}
