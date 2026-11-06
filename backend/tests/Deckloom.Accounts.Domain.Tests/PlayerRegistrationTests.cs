using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-015 (REQ-001, REQ-006, REQ-022, REQ-033, REQ-034, REQ-044, REQ-048; J1-J4, J6): fabricas de cadastro do Player.
// Design (emenda 2026-10-23, "Fabricas de cadastro"): value objects ja construidos, dados brutos de aceite,
// `DateTimeOffset now` e `defaultPresetId`; devolvem Result<Player>; erros de TermsAcceptance e de Avatar repassados
// sem alteracao; PlayerId gerado pela fabrica; evento PlayerRegistered(PlayerId, RegistrationMethod, TermsVersion).
// As regras do nome (inclusive o inicial do Google) sao do DisplayName e ja tem testes proprios.
public class PlayerRegistrationTests
{
    private const string Current = "2026-10-01";
    private const string PresetId = "preset-01";
    private const string Subject = "google-subject-123";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static Email AnEmail() => Email.Create("ana@example.com").Value;

    private static PasswordHash AHash() => PasswordHash.Parse(Stored).Value;

    private static ExternalIdentity AGoogleIdentity() => ExternalIdentity.Create("google", Subject).Value;

    private static DisplayName AName() => DisplayName.Create("Ana").Value;

    private static Result<Player> RegisterPassword(
        string? submitted = Current,
        bool accept = true,
        bool adult = true,
        string? presetId = PresetId) =>
        Player.RegisterWithPassword(AnEmail(), AHash(), AName(), Current, submitted, accept, adult, Now, presetId);

    private static Result<Player> RegisterGoogle(
        string? submitted = Current,
        bool accept = true,
        bool adult = true,
        string? presetId = PresetId) =>
        Player.RegisterWithGoogle(AnEmail(), AGoogleIdentity(), AName(), Current, submitted, accept, adult, Now, presetId);

    // J2 / REQ-001 / REQ-006

    [Fact]
    public void REQ_001_password_account_has_a_password_hash_and_no_external_identity()
    {
        var player = RegisterPassword().Value;

        Assert.NotNull(player.PasswordHash);
        Assert.Equal(Stored, player.PasswordHash.ToStoredString());
        Assert.Null(player.ExternalIdentity);
    }

    [Fact]
    public void REQ_006_google_account_has_an_external_identity_and_no_password_hash()
    {
        var player = RegisterGoogle().Value;

        Assert.Equal(AGoogleIdentity(), player.ExternalIdentity);
        Assert.Null(player.PasswordHash);
    }

    [Fact]
    public void REQ_001_the_account_keeps_the_given_email_and_display_name()
    {
        var player = RegisterPassword().Value;

        Assert.Equal(AnEmail(), player.Email);
        Assert.Equal("Ana", player.DisplayName.Value);
    }

    [Fact]
    public void REQ_006_the_google_account_keeps_the_given_email_and_display_name()
    {
        var player = RegisterGoogle().Value;

        Assert.Equal(AnEmail(), player.Email);
        Assert.Equal("Ana", player.DisplayName.Value);
    }

    // Identidade

    [Fact]
    public void Each_registration_generates_a_non_empty_distinct_player_id()
    {
        var first = RegisterPassword().Value;
        var second = RegisterPassword().Value;
        var google = RegisterGoogle().Value;

        Assert.NotEqual(Guid.Empty, first.Id.Value);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Id, google.Id);
    }

    // J4 / J6 defaults (ambas as vias)

    [Fact]
    public void REQ_044_password_account_is_born_with_mtg_as_favorite_tcg()
    {
        Assert.Equal(FavoriteTcg.Default, RegisterPassword().Value.FavoriteTcg);
        Assert.Equal("mtg", RegisterPassword().Value.FavoriteTcg.Id);
    }

    [Fact]
    public void REQ_044_google_account_is_born_with_mtg_as_favorite_tcg()
    {
        Assert.Equal("mtg", RegisterGoogle().Value.FavoriteTcg.Id);
    }

    [Fact]
    public void REQ_022_password_account_is_born_with_the_default_preset_avatar()
    {
        var avatar = Assert.IsType<PresetAvatar>(RegisterPassword().Value.Avatar);

        Assert.Equal(PresetId, avatar.PresetId);
    }

    [Fact]
    public void REQ_022_google_account_is_born_with_the_default_preset_avatar()
    {
        var avatar = Assert.IsType<PresetAvatar>(RegisterGoogle().Value.Avatar);

        Assert.Equal(PresetId, avatar.PresetId);
    }

    [Fact]
    public void REQ_015_both_accounts_are_born_without_preferred_formats()
    {
        Assert.Empty(RegisterPassword().Value.Formats.Ids);
        Assert.Empty(RegisterGoogle().Value.Formats.Ids);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void REQ_022_blank_default_preset_id_fails_with_invalid_for_password_registration(string? presetId)
    {
        var result = RegisterPassword(presetId: presetId);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("invalid"), result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void REQ_022_blank_default_preset_id_fails_with_invalid_for_google_registration(string? presetId)
    {
        var result = RegisterGoogle(presetId: presetId);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("invalid"), result.Error);
    }

    // Aceite de termos e 18+

    [Fact]
    public void REQ_033_password_account_records_terms_version_date_and_adult_declaration()
    {
        var terms = RegisterPassword().Value.TermsAcceptance;

        Assert.Equal(Current, terms.TermsVersion);
        Assert.Equal(Now, terms.AcceptedAt);
        Assert.True(terms.AdultDeclared);
    }

    [Fact]
    public void REQ_048_google_account_records_terms_version_date_and_adult_declaration()
    {
        var terms = RegisterGoogle().Value.TermsAcceptance;

        Assert.Equal(Current, terms.TermsVersion);
        Assert.Equal(Now, terms.AcceptedAt);
        Assert.True(terms.AdultDeclared);
    }

    [Fact]
    public void REQ_033_password_registration_without_acceptance_is_not_created()
    {
        var result = RegisterPassword(accept: false);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("required", "acceptTerms"), result.Error);
    }

    [Fact]
    public void REQ_033_google_registration_without_acceptance_is_not_created()
    {
        var result = RegisterGoogle(accept: false);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("required", "acceptTerms"), result.Error);
    }

    [Fact]
    public void REQ_034_password_registration_without_adult_declaration_is_not_created()
    {
        var result = RegisterPassword(adult: false);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("required", "adultDeclaration"), result.Error);
    }

    [Fact]
    public void REQ_034_google_registration_without_adult_declaration_is_not_created()
    {
        var result = RegisterGoogle(adult: false);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("required", "adultDeclaration"), result.Error);
    }

    [Fact]
    public void REQ_043_password_registration_with_another_terms_version_is_not_created()
    {
        var result = RegisterPassword(submitted: "2026-09-01");

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("version_mismatch", "acceptTerms"), result.Error);
    }

    [Fact]
    public void REQ_043_google_registration_with_another_terms_version_is_not_created()
    {
        var result = RegisterGoogle(submitted: null);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("version_mismatch", "acceptTerms"), result.Error);
    }

    [Fact]
    public void REQ_033_terms_errors_keep_the_fixed_order_through_the_factories()
    {
        Assert.Equal(
            new DomainError("required", "acceptTerms"),
            RegisterPassword(submitted: "x", accept: false, adult: false).Error);
        Assert.Equal(
            new DomainError("version_mismatch", "acceptTerms"),
            RegisterGoogle(submitted: "x", adult: false).Error);
    }

    // Precedencia: erro de aceite vence erro de preset padrao (decisao do usuario, design.md, "Fabricas de cadastro")

    [Fact]
    public void REQ_033_terms_error_wins_over_blank_default_preset_id_on_password_registration()
    {
        var result = RegisterPassword(accept: false, presetId: " ");

        Assert.Equal(new DomainError("required", "acceptTerms"), result.Error);
    }

    [Fact]
    public void REQ_033_terms_error_wins_over_blank_default_preset_id_on_google_registration()
    {
        var result = RegisterGoogle(adult: false, presetId: null);

        Assert.Equal(new DomainError("required", "adultDeclaration"), result.Error);
    }

    // Evento PlayerRegistered

    [Fact]
    public void REQ_001_password_registration_emits_player_registered_with_method_password()
    {
        var player = RegisterPassword().Value;

        var emitted = Assert.IsType<PlayerRegistered>(Assert.Single(player.DomainEvents));
        Assert.Equal(new PlayerRegistered(player.Id, RegistrationMethod.Password, Current), emitted);
    }

    [Fact]
    public void REQ_006_google_registration_emits_player_registered_with_method_google()
    {
        var player = RegisterGoogle().Value;

        var emitted = Assert.IsType<PlayerRegistered>(Assert.Single(player.DomainEvents));
        Assert.Equal(new PlayerRegistered(player.Id, RegistrationMethod.Google, Current), emitted);
    }

    [Fact]
    public void REQ_001_player_registered_carries_no_email_hash_or_other_personal_data()
    {
        var properties = typeof(PlayerRegistered).GetProperties()
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["Method", "OccurredAt", "PlayerId", "TermsVersion"], properties);
    }

    // R-07: J2 garantido em runtime, nao so por anotacao de nulidade

    [Fact]
    public void REQ_001_register_with_password_rejects_null_email()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Player.RegisterWithPassword(null!, AHash(), AName(), Current, Current, true, true, Now, PresetId));
    }

    [Fact]
    public void REQ_001_register_with_password_rejects_null_password_hash()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Player.RegisterWithPassword(AnEmail(), null!, AName(), Current, Current, true, true, Now, PresetId));
    }

    [Fact]
    public void REQ_001_register_with_password_rejects_null_display_name()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Player.RegisterWithPassword(AnEmail(), AHash(), null!, Current, Current, true, true, Now, PresetId));
    }

    [Fact]
    public void REQ_006_register_with_google_rejects_null_email()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Player.RegisterWithGoogle(null!, AGoogleIdentity(), AName(), Current, Current, true, true, Now, PresetId));
    }

    [Fact]
    public void REQ_006_register_with_google_rejects_null_external_identity()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Player.RegisterWithGoogle(AnEmail(), null!, AName(), Current, Current, true, true, Now, PresetId));
    }

    [Fact]
    public void REQ_006_register_with_google_rejects_null_display_name()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Player.RegisterWithGoogle(AnEmail(), AGoogleIdentity(), null!, Current, Current, true, true, Now, PresetId));
    }
}
