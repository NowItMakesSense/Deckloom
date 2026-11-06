using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-031 (REQ-003, 033, 034, 038, 040, 043): validacao por campo do cadastro com senha, agregada antes de hash e persistencia.
// Forma esperada de Result.Errors: lista de DomainError(Code, Field) com Field no nome do contrato da API
// ("email", "password", "displayName", "acceptTerms", "adultDeclaration"); o caso de uso preenche o Field dos
// erros de Email/Password/DisplayName (os value objects os devolvem sem Field). Ordem: email, password, displayName, termos.
public class RegisterWithPasswordValidationTests
{
    private const string Ip = "203.0.113.7";
    private const string TermsVersion = "2026-10-01";
    private const string RawEmail = "ana@example.com";
    private const string RawPassword = "correct horse battery";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly InMemoryAccessAttemptStore _attempts;
    private readonly FakePasswordHasher _hasher = new(600_000);
    private readonly SequentialTokenGenerator _tokens = new();
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeKeyedHasher _keyedHasher = new();
    private readonly RecordingDelayer _delayer = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    private readonly FixedTermsVersionProvider _terms = new(
        TermsVersion, new Uri("https://deckloom.test/termos"), new Uri("https://deckloom.test/privacidade"));

    public RegisterWithPasswordValidationTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _attempts = new InMemoryAccessAttemptStore(_uow);
    }

    public static TheoryData<string?, string> InvalidEmails => new()
    {
        { null, "required" },
        { "", "required" },
        { "   ", "required" },
        { "not-an-email", "invalid_format" },
        { "ana@", "invalid_format" },
    };

    public static TheoryData<string?, string> InvalidPasswords => new()
    {
        { null, "required" },
        { "", "required" },
        { new string('a', 7), "too_short" },
        { new string('a', 129), "too_long" },
    };

    private static RegisterWithPasswordCommand ValidCommand() =>
        new(RawEmail, RawPassword, "Ana", true, TermsVersion, true, Ip);

    private RegisterWithPassword NewUseCase() =>
        new(_players, _sessions, _uow, _hasher, _tokens, _tokenHasher, _terms, _catalog, NewThrottle(), _clock);

    private AccessThrottle NewThrottle() =>
        new(_attempts, _keyedHasher, _delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock);

    private async Task AssertNothingCreatedAsync()
    {
        Assert.Null(await _players.FindByEmail(AnEmail(RawEmail)));
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
        Assert.Empty(_hasher.HashCalls);
    }

    [Theory]
    [MemberData(nameof(InvalidEmails))]
    public async Task REQ003_invalid_or_empty_email_is_rejected_on_the_email_field_and_nothing_is_created(string? email, string code)
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { Email = email });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError(code, "email"), Assert.Single(result.Errors));
        await AssertNothingCreatedAsync();
    }

    [Theory]
    [MemberData(nameof(InvalidPasswords))]
    public async Task REQ003_REQ038_invalid_password_is_rejected_on_the_password_field_without_computing_a_hash(string? password, string code)
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { Password = password });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError(code, "password"), Assert.Single(result.Errors));
        await AssertNothingCreatedAsync();
    }

    [Theory]
    [InlineData("aaaaaaaa")]
    [InlineData("a8-chars")]
    public async Task REQ038_password_at_the_lower_boundary_of_eight_characters_is_accepted_even_when_weak(string password)
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { Password = password });

        Assert.True(result.IsSuccess);
        Assert.Single(_hasher.HashCalls);
    }

    [Fact]
    public async Task REQ038_password_of_128_characters_is_accepted()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { Password = new string('a', 128) });

        Assert.True(result.IsSuccess);
        Assert.Single(_hasher.HashCalls);
    }

    [Theory]
    [InlineData("Ana\u0007")]
    [InlineData("A\u0000na")]
    [InlineData("Ana\u007F")]
    [InlineData("An\na")]
    public async Task REQ040_display_name_with_a_control_character_is_rejected_on_the_display_name_field(string name)
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { DisplayName = name });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("invalid_characters", "displayName"), Assert.Single(result.Errors));
        await AssertNothingCreatedAsync();
    }

    [Fact]
    public async Task REQ033_missing_terms_acceptance_is_rejected_on_the_accept_terms_field()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { AcceptTerms = false });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("required", "acceptTerms"), Assert.Single(result.Errors));
        await AssertNothingCreatedAsync();
    }

    [Fact]
    public async Task REQ034_missing_adult_declaration_is_rejected_on_the_adult_declaration_field()
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { AdultDeclaration = false });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("required", "adultDeclaration"), Assert.Single(result.Errors));
        await AssertNothingCreatedAsync();
    }

    [Theory]
    [InlineData("2025-01-01")]
    [InlineData(null)]
    [InlineData("")]
    public async Task REQ043_outdated_or_missing_terms_version_is_rejected_with_version_mismatch_on_accept_terms(string? version)
    {
        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { TermsVersion = version });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("version_mismatch", "acceptTerms"), Assert.Single(result.Errors));
        await AssertNothingCreatedAsync();
    }

    [Fact]
    public async Task REQ003_all_field_errors_are_returned_together_in_a_fixed_order()
    {
        var command = new RegisterWithPasswordCommand("not-an-email", "short", "Ana\u0007", false, "2025-01-01", false, Ip);

        var result = await NewUseCase().ExecuteAsync(command);

        Assert.True(result.IsFailure);
        Assert.Equal(
            new[]
            {
                new DomainError("invalid_format", "email"),
                new DomainError("too_short", "password"),
                new DomainError("invalid_characters", "displayName"),
                new DomainError("required", "acceptTerms"),
            },
            result.Errors);
        await AssertNothingCreatedAsync();
    }

    [Fact]
    public async Task REQ003_REQ043_a_terms_error_is_returned_together_with_a_password_error()
    {
        var command = ValidCommand() with { Password = new string('a', 129), TermsVersion = "2025-01-01" };

        var result = await NewUseCase().ExecuteAsync(command);

        Assert.True(result.IsFailure);
        Assert.Equal(
            new[] { new DomainError("too_long", "password"), new DomainError("version_mismatch", "acceptTerms") },
            result.Errors);
        await AssertNothingCreatedAsync();
    }

    [Fact]
    public async Task REQ003_REQ034_an_adult_declaration_error_is_returned_together_with_an_email_error()
    {
        var command = ValidCommand() with { Email = "", AdultDeclaration = false };

        var result = await NewUseCase().ExecuteAsync(command);

        Assert.True(result.IsFailure);
        Assert.Equal(
            new[] { new DomainError("required", "email"), new DomainError("required", "adultDeclaration") },
            result.Errors);
        await AssertNothingCreatedAsync();
    }

    [Fact]
    public async Task REQ033_a_terms_error_alone_does_not_compute_a_hash_even_though_the_password_is_valid()
    {
        await NewUseCase().ExecuteAsync(ValidCommand() with { AcceptTerms = false });

        Assert.Empty(_hasher.HashCalls);
    }
}
