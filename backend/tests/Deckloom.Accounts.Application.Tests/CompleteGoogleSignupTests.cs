using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-041 (REQ-006, 022, 033, 034, 039, 040, 043, 044, 048): conclusao do cadastro Google. RED: os tipos abaixo ainda nao existem.
// Contrato (design.md linha 1534, POST /auth/google/complete-signup): body {displayName, acceptTerms, termsVersion, adultDeclaration};
//   201 SessionSummary + cookie de sessao; 400 validation_failed (displayName length/invalid_characters, acceptTerms
//   required/version_mismatch, adultDeclaration required; pendencia nao consumida); 404 pending_not_found.
// Forma esperada (convencao do T-030/T-031/T-040; a spec nao fixa nomes de tipos):
//   sealed record CompleteGoogleSignupCommand(string? PendingToken, string? DisplayName, bool AcceptTerms,
//       string? TermsVersion, bool AdultDeclaration)       <- token do cookie __Host-dl_pending EM CLARO (design.md linhas 266, 474)
//   sealed class CompleteGoogleSignup(IPlayerRepository, ISessionRepository, IUnitOfWork, ISecureTokenGenerator, ITokenHasher,
//       ITermsVersionProvider, IAvatarPresetCatalog, IPendingGoogleAuthStore, TimeProvider)
//     Task<Result<AuthenticatedSession>> ExecuteAsync(CompleteGoogleSignupCommand, CancellationToken ct = default)
//   Sem Ip/AccessThrottle: a conclusao nao e tentativa de acesso (AccessAction nao a cobre; T-042 trata uso unico/corrida).
// Ordem do caso de uso: FindAsync (nao consome) -> valida nome e termos (ordem displayName, termos; Field no contrato da API)
//   -> UoW { ConsumeAsync + Player.RegisterWithGoogle + Add player + Add session }. Email e subject vem da pendencia.
public class CompleteGoogleSignupTests
{
    private const string TermsVersion = "2026-10-01";
    private const string PendingToken = "pending-raw-token";
    private const string Subject = "google-sub-1";
    private const string GoogleEmail = "bia@example.com";
    private const string DefaultPresetId = "preset-02";
    private const string FamilyEmoji = "👨‍👩‍👧";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly PendingStoreSpy _pending;
    private readonly SequentialTokenGenerator _tokens = new();
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry(DefaultPresetId, "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    private readonly FixedTermsVersionProvider _terms = new(
        TermsVersion, new Uri("https://deckloom.test/termos"), new Uri("https://deckloom.test/privacidade"));

    public CompleteGoogleSignupTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _pending = new PendingStoreSpy(new InMemoryPendingGoogleAuthStore(_clock, _uow), _uow);
    }

    private CompleteGoogleSignup NewUseCase() =>
        new(_players, _sessions, _uow, _tokens, _tokenHasher, _terms, _catalog, _pending, _clock);

    private static CompleteGoogleSignupCommand ValidCommand() =>
        new(PendingToken, "Beatriz", true, TermsVersion, true);

    private async Task SeedSignupAsync(string? suggestedName = "Beatriz Souza") =>
        await _pending.AddAsync(PendingGoogleSignup.Create(
            _tokenHasher.Hash(PendingToken), Subject, AnEmail(GoogleEmail), suggestedName, Now));

    private async Task<Player?> StoredPlayerAsync() => await _players.FindByEmail(AnEmail(GoogleEmail));

    private async Task<bool> PendingStillStoredAsync() =>
        await _pending.FindAsync(_tokenHasher.Hash(PendingToken)) is not null;

    private async Task AssertRejectedWithoutConsumingAsync()
    {
        Assert.Null(await StoredPlayerAsync());
        Assert.Null(await _players.FindByGoogleSubject(Subject));
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
        Assert.Equal(0, _pending.ConsumeCalls);
        Assert.True(await PendingStillStoredAsync());
    }

    [Fact]
    public async Task REQ006_valid_completion_creates_a_google_account_without_a_local_password()
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        var player = (await StoredPlayerAsync())!;
        Assert.NotNull(player);
        Assert.Equal("Beatriz", player.DisplayName.Value);
        Assert.Null(player.PasswordHash);
        Assert.Equal("google", player.ExternalIdentity!.Provider);
        Assert.Equal(Subject, player.ExternalIdentity.Subject);
        Assert.Same(player, await _players.FindByGoogleSubject(Subject));
    }

    [Fact]
    public async Task REQ006_the_account_email_comes_from_the_pending_and_not_from_the_command()
    {
        await SeedSignupAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal(AnEmail(GoogleEmail), (await StoredPlayerAsync())!.Email);
    }

    [Fact]
    public async Task REQ022_the_new_account_gets_the_catalog_default_preset_avatar_and_the_google_photo_is_ignored()
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        var avatar = Assert.IsType<PresetAvatar>((await StoredPlayerAsync())!.Avatar);
        Assert.Equal(DefaultPresetId, avatar.PresetId);
        Assert.Equal("preset", result.Value.Summary.Avatar.Kind);
        Assert.Equal(DefaultPresetId, result.Value.Summary.Avatar.PresetId);
        Assert.Equal("/avatars/preset-02.png", result.Value.Summary.Avatar.ImageUrl);
    }

    [Fact]
    public async Task REQ044_the_new_account_has_magic_as_favorite_tcg_and_no_formats()
    {
        await SeedSignupAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        var player = (await StoredPlayerAsync())!;
        Assert.Equal(FavoriteTcg.Default, player.FavoriteTcg);
        Assert.Equal(FormatPreferences.Empty, player.Formats);
    }

    [Fact]
    public async Task REQ033_REQ048_records_the_terms_acceptance_with_version_time_and_adult_declaration()
    {
        await SeedSignupAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));

        await NewUseCase().ExecuteAsync(ValidCommand());

        var terms = (await StoredPlayerAsync())!.TermsAcceptance;
        Assert.Equal(TermsVersion, terms.TermsVersion);
        Assert.Equal(Now.AddMinutes(5), terms.AcceptedAt);
        Assert.True(terms.AdultDeclared);
    }

    [Fact]
    public async Task REQ006_opens_a_session_without_remember_me_and_returns_the_session_summary()
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        var player = (await StoredPlayerAsync())!;
        var session = await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001"));
        Assert.NotNull(session);
        Assert.Equal(player.Id, session!.PlayerId);
        Assert.False(session.RememberMe);
        Assert.Equal(Now, session.CreatedAt);
        Assert.Equal("tok-0001", result.Value.SessionToken);
        Assert.Equal(session.ExpiresAt, result.Value.ExpiresAt);
        Assert.Equal("Beatriz", result.Value.Summary.DisplayName);
        Assert.False(result.Value.Summary.HasPassword);
        Assert.True(result.Value.Summary.HasGoogle);
    }

    [Fact]
    public async Task REQ039_the_pending_is_consumed_on_success_and_cannot_be_read_again()
    {
        await SeedSignupAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal(1, _pending.ConsumeCalls);
        Assert.False(await PendingStillStoredAsync());
    }

    [Fact]
    public async Task REQ039_consumes_the_pending_in_the_same_unit_of_work_that_persists_player_and_session()
    {
        await SeedSignupAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(Assert.Single(_pending.TransactionOpenAtConsume));
        Assert.True(_uow.TransactionOpenDuringLastCommit);
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task REQ039_the_suggested_google_name_is_not_used_when_the_player_edits_the_name()
    {
        await SeedSignupAsync(suggestedName: new string('B', 40));

        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { DisplayName = "Bia" });

        Assert.True(result.IsSuccess);
        Assert.Equal("Bia", (await StoredPlayerAsync())!.DisplayName.Value);
    }

    [Fact]
    public async Task REQ039_two_letters_plus_a_composed_emoji_are_accepted_as_three_text_elements()
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { DisplayName = "Bi" + FamilyEmoji });

        Assert.True(result.IsSuccess);
        Assert.Equal("Bi" + FamilyEmoji, (await StoredPlayerAsync())!.DisplayName.Value);
    }

    [Fact]
    public async Task REQ039_a_name_with_surrounding_spaces_is_trimmed_before_it_is_stored()
    {
        await SeedSignupAsync();

        await NewUseCase().ExecuteAsync(ValidCommand() with { DisplayName = "  Bia  " });

        Assert.Equal("Bia", (await StoredPlayerAsync())!.DisplayName.Value);
    }

    [Fact]
    public async Task REQ039_a_two_letter_name_with_surrounding_spaces_is_rejected_with_length_and_the_pending_is_kept()
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { DisplayName = "  Bi  " });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("length", "displayName"), Assert.Single(result.Errors));
        await AssertRejectedWithoutConsumingAsync();
    }

    [Fact]
    public async Task REQ039_the_forty_character_google_name_submitted_unedited_is_rejected_with_length_and_the_pending_is_kept()
    {
        var googleName = new string('B', 40);
        await SeedSignupAsync(googleName);

        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { DisplayName = googleName });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("length", "displayName"), Assert.Single(result.Errors));
        await AssertRejectedWithoutConsumingAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task REQ039_a_missing_name_is_rejected_with_length_on_the_display_name_field(string? name)
    {
        await SeedSignupAsync(suggestedName: null);

        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { DisplayName = name });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("length", "displayName"), Assert.Single(result.Errors));
        await AssertRejectedWithoutConsumingAsync();
    }

    [Theory]
    [InlineData("Bia\u0007")]
    [InlineData("B\u0000ia")]
    [InlineData("Bia\u007F")]
    [InlineData("Bi\na")]
    public async Task REQ040_a_name_with_a_control_character_is_rejected_and_the_pending_is_kept(string name)
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { DisplayName = name });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("invalid_characters", "displayName"), Assert.Single(result.Errors));
        await AssertRejectedWithoutConsumingAsync();
    }

    [Fact]
    public async Task REQ033_missing_terms_acceptance_is_rejected_on_the_accept_terms_field_and_the_pending_is_kept()
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { AcceptTerms = false });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("required", "acceptTerms"), Assert.Single(result.Errors));
        await AssertRejectedWithoutConsumingAsync();
    }

    [Fact]
    public async Task REQ034_missing_adult_declaration_is_rejected_on_the_adult_declaration_field_and_the_pending_is_kept()
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { AdultDeclaration = false });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("required", "adultDeclaration"), Assert.Single(result.Errors));
        await AssertRejectedWithoutConsumingAsync();
    }

    [Theory]
    [InlineData("2025-01-01")]
    [InlineData(null)]
    [InlineData("")]
    public async Task REQ043_outdated_or_missing_terms_version_is_rejected_with_version_mismatch_and_the_pending_is_kept(string? version)
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { TermsVersion = version });

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("version_mismatch", "acceptTerms"), Assert.Single(result.Errors));
        await AssertRejectedWithoutConsumingAsync();
    }

    [Fact]
    public async Task REQ039_REQ033_REQ034_all_field_errors_are_returned_together_in_name_then_terms_order()
    {
        await SeedSignupAsync();
        var command = new CompleteGoogleSignupCommand(PendingToken, "Bi", false, TermsVersion, false);

        var result = await NewUseCase().ExecuteAsync(command);

        Assert.True(result.IsFailure);
        Assert.Equal(
            new[]
            {
                new DomainError("length", "displayName"),
                new DomainError("required", "acceptTerms"),
                new DomainError("required", "adultDeclaration"),
            },
            result.Errors);
        await AssertRejectedWithoutConsumingAsync();
    }

    [Theory]
    [InlineData("another-token")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task REQ006_an_unknown_or_missing_pending_token_is_pending_not_found_and_creates_nothing(string? token)
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { PendingToken = token });

        Assert.True(result.IsFailure);
        Assert.Equal("pending_not_found", result.Error.Code);
        Assert.Null(await StoredPlayerAsync());
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
        Assert.True(await PendingStillStoredAsync());
    }

    [Fact]
    public async Task REQ006_a_pending_link_cannot_complete_a_signup_and_is_not_consumed()
    {
        await _pending.AddAsync(PendingGoogleLink.Create(
            _tokenHasher.Hash(PendingToken), Subject, AnEmail(GoogleEmail), new PlayerId(Guid.NewGuid()), Now));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsFailure);
        Assert.Equal("pending_not_found", result.Error.Code);
        Assert.Null(await StoredPlayerAsync());
        Assert.Equal(0, _pending.ConsumeCalls);
        Assert.True(await PendingStillStoredAsync());
    }

    // Decorator que expoe se o consumo aconteceu dentro do bloco do IUnitOfWork e quantas vezes foi chamado.
    private sealed class PendingStoreSpy(IPendingGoogleAuthStore inner, InMemoryUnitOfWork uow) : IPendingGoogleAuthStore
    {
        private readonly List<bool> _transactionOpenAtConsume = [];

        public int ConsumeCalls => _transactionOpenAtConsume.Count;

        public IReadOnlyList<bool> TransactionOpenAtConsume => _transactionOpenAtConsume;

        public Task AddAsync(PendingGoogleAuth pending, CancellationToken ct = default) => inner.AddAsync(pending, ct);

        public Task<PendingGoogleAuth?> FindAsync(TokenHash tokenHash, CancellationToken ct = default) =>
            inner.FindAsync(tokenHash, ct);

        public Task<PendingGoogleAuth?> ConsumeAsync(TokenHash tokenHash, CancellationToken ct = default)
        {
            _transactionOpenAtConsume.Add(uow.IsTransactionOpen);
            return inner.ConsumeAsync(tokenHash, ct);
        }

        public Task<int> DeleteExpiredAsync(int batchSize, CancellationToken ct = default) => inner.DeleteExpiredAsync(batchSize, ct);
    }
}
