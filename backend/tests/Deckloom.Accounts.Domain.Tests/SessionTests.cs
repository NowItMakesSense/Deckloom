using System.Reflection;
using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-021 (REQ-010, REQ-011, REQ-012, REQ-030, REQ-035, REQ-042; S1-S6): agregado Session.
// Design (emenda 2026-10-23, bloco "Agregado Session"):
//   Start(PlayerId, TokenHash, rememberMe, now) emite SessionStarted(SessionId, PlayerId, RememberMe); SessionId e UUIDv7.
//   ExpiresAt = now + 30 min (sem lembrar-me) ou now + 30 dias (lembrar-me). IsValid(now): nao revogada e now < ExpiresAt.
//   Touch(now): valida -> renova LastSeenAt (e ExpiresAt sem lembrar-me) e devolve true; invalida -> nada muda, false.
//   Revoke(reason, now): 1a vez marca RevokedAt e emite SessionRevoked (mesmo expirada); depois, idempotente.
//   MarkReauthenticated(linked, presented, now): session_invalid, depois subject_mismatch; Field nulo, sem mudanca de estado.
//   ToRecentGoogleLogin(): RecentGoogleLogin(PlayerId, ReauthenticatedAt); a janela de 5 min e do Player.Delete.
public class SessionTests
{
    private const string Current = "2026-10-01";

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly PlayerId Owner = new(Guid.NewGuid());

    private static TokenHash AHash(byte fill = 7) =>
        TokenHash.Create(Enumerable.Repeat(fill, TokenHash.Length).ToArray()).Value;

    private static Session AStartedSession(bool rememberMe = false, PlayerId? player = null, TokenHash? hash = null)
    {
        var session = Session.Start(player ?? Owner, hash ?? AHash(), rememberMe, Now);
        session.ClearDomainEvents();
        return session;
    }

    private static ExternalIdentity Google(string subject = "google-subject-1") =>
        ExternalIdentity.Create("google", subject).Value;

    private static Player AGoogleOnlyPlayer()
    {
        var player = Player.RegisterWithGoogle(
            Email.Create("ana@example.com").Value,
            Google(),
            DisplayName.Create("Ana").Value,
            Current,
            Current,
            true,
            true,
            Now,
            "preset-01").Value;
        player.ClearDomainEvents();
        return player;
    }

    private static void AssertSingleError(Result<Session> result, string code)
    {
        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Null(error.Field);
    }

    private static (string Name, Type Type)[] DeclaredProperties(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(property => (property.Name, property.PropertyType))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    // Inicio (S1, S2)

    [Fact]
    public void REQ_010_a_started_session_records_player_hash_and_timestamps()
    {
        var hash = AHash();

        var session = Session.Start(Owner, hash, false, Now);

        Assert.Equal(Owner, session.PlayerId);
        Assert.Same(hash, session.TokenHash);
        Assert.False(session.RememberMe);
        Assert.Equal(Now, session.CreatedAt);
        Assert.Equal(Now, session.LastSeenAt);
        Assert.Null(session.RevokedAt);
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public void REQ_012_without_remember_me_the_session_expires_thirty_minutes_after_start()
    {
        var session = Session.Start(Owner, AHash(), false, Now);

        Assert.Equal(Now.AddMinutes(30), session.ExpiresAt);
    }

    [Fact]
    public void REQ_035_with_remember_me_the_session_expires_thirty_days_after_start()
    {
        var session = Session.Start(Owner, AHash(), true, Now);

        Assert.True(session.RememberMe);
        Assert.Equal(Now.AddDays(30), session.ExpiresAt);
    }

    [Fact]
    public void REQ_010_starting_a_session_emits_only_SessionStarted_with_ids_and_remember_me()
    {
        var session = Session.Start(Owner, AHash(), true, Now);

        var started = Assert.IsType<SessionStarted>(Assert.Single(session.DomainEvents));
        Assert.Equal(session.Id, started.SessionId);
        Assert.Equal(Owner, started.PlayerId);
        Assert.True(started.RememberMe);
    }

    [Fact]
    public void REQ_010_the_SessionStarted_event_carries_only_ids_and_remember_me()
    {
        Assert.Equal(
            [("PlayerId", typeof(PlayerId)), ("RememberMe", typeof(bool)), ("SessionId", typeof(SessionId))],
            DeclaredProperties(typeof(SessionStarted)));
    }

    [Fact]
    public void REQ_010_the_SessionRevoked_event_carries_only_ids_and_reason()
    {
        Assert.Equal(
            [
                ("PlayerId", typeof(PlayerId)),
                ("Reason", typeof(SessionRevocationReason)),
                ("SessionId", typeof(SessionId)),
            ],
            DeclaredProperties(typeof(SessionRevoked)));
    }

    [Fact]
    public void REQ_010_the_revocation_reasons_are_exactly_Logout_PasswordReset_and_AccountDeletion()
    {
        Assert.Equal(
            ["AccountDeletion", "Logout", "PasswordReset"],
            Enum.GetNames<SessionRevocationReason>().OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void REQ_010_the_session_id_is_a_version_7_guid()
    {
        var session = Session.Start(Owner, AHash(), false, Now);

        Assert.Equal(7, session.Id.Value.Version);
    }

    [Fact]
    public void REQ_010_session_ids_are_unique_across_sessions()
    {
        var ids = Enumerable.Range(0, 500)
            .Select(_ => Session.Start(Owner, AHash(), false, Now).Id)
            .ToHashSet();

        Assert.Equal(500, ids.Count);
    }

    [Fact]
    public void REQ_010_only_the_token_hash_is_stored_never_a_clear_token()
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var properties = typeof(Session).GetProperties(all).Select(p => (p.Name, p.PropertyType)).ToArray();
        var fields = typeof(Session).GetFields(all).Select(f => f.FieldType).ToArray();

        Assert.Contains(("TokenHash", typeof(TokenHash)), properties);
        Assert.DoesNotContain(properties, p => p.PropertyType == typeof(string) || p.PropertyType == typeof(byte[]));
        Assert.DoesNotContain(fields, f => f == typeof(string) || f == typeof(byte[]));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Token", StringComparison.Ordinal) && p.Name != "TokenHash");
    }

    [Fact]
    public void REQ_010_Start_takes_the_hash_and_never_a_clear_token()
    {
        var start = typeof(Session).GetMethod(nameof(Session.Start))!;

        var parameterTypes = start.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Contains(typeof(TokenHash), parameterTypes);
        Assert.DoesNotContain(typeof(string), parameterTypes);
        Assert.DoesNotContain(typeof(byte[]), parameterTypes);
    }

    [Fact]
    public void REQ_010_the_session_state_has_no_public_setters()
    {
        var mutable = typeof(Session)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToArray();

        Assert.Empty(mutable);
    }

    [Fact]
    public void REQ_010_the_session_has_no_public_constructor_and_no_public_fields()
    {
        Assert.Empty(typeof(Session).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(Session).GetFields(BindingFlags.Public | BindingFlags.Instance));
    }

    // Validade sem lembrar-me (S3, REQ-012)

    [Theory]
    [InlineData(0)]
    [InlineData(29 * 60)]
    [InlineData((30 * 60) - 1)]
    public void REQ_012_without_remember_me_the_session_is_valid_before_thirty_minutes_of_inactivity(int seconds)
    {
        var session = AStartedSession();

        Assert.True(session.IsValid(Now.AddSeconds(seconds)));
    }

    [Theory]
    [InlineData(30 * 60)]
    [InlineData(31 * 60)]
    [InlineData(24 * 3600)]
    public void REQ_012_without_remember_me_the_session_is_expired_from_exactly_thirty_minutes(int seconds)
    {
        var session = AStartedSession();

        Assert.False(session.IsValid(Now.AddSeconds(seconds)));
    }

    [Fact]
    public void REQ_012_each_valid_use_renews_the_inactivity_window()
    {
        var session = AStartedSession();

        var first = session.Touch(Now.AddMinutes(20));
        var validAtFortyFive = session.IsValid(Now.AddMinutes(45));
        var second = session.Touch(Now.AddMinutes(45));

        Assert.True(first);
        Assert.True(validAtFortyFive);
        Assert.True(second);
        Assert.True(session.IsValid(Now.AddMinutes(74)));
        Assert.False(session.IsValid(Now.AddMinutes(75)));
    }

    [Fact]
    public void REQ_012_without_a_touch_the_session_is_already_expired_at_forty_five_minutes()
    {
        var session = AStartedSession();

        Assert.False(session.IsValid(Now.AddMinutes(45)));
    }

    [Fact]
    public void REQ_012_touch_renews_last_seen_at_and_expires_at_without_remember_me()
    {
        var session = AStartedSession();
        var at = Now.AddMinutes(20);

        session.Touch(at);

        Assert.Equal(at, session.LastSeenAt);
        Assert.Equal(at.AddMinutes(30), session.ExpiresAt);
        Assert.Equal(Now, session.CreatedAt);
    }

    // Validade com lembrar-me (S3, REQ-035)

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(29)]
    public void REQ_035_with_remember_me_the_session_is_valid_for_days_before_thirty(int days)
    {
        var session = AStartedSession(rememberMe: true);

        Assert.True(session.IsValid(Now.AddDays(days)));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void REQ_035_with_remember_me_the_session_is_expired_from_exactly_thirty_days(int days)
    {
        var session = AStartedSession(rememberMe: true);

        Assert.False(session.IsValid(Now.AddDays(days)));
    }

    [Fact]
    public void REQ_035_with_remember_me_touch_renews_last_seen_at_but_does_not_extend_validity()
    {
        var session = AStartedSession(rememberMe: true);
        var at = Now.AddDays(10);

        var touched = session.Touch(at);

        Assert.True(touched);
        Assert.Equal(at, session.LastSeenAt);
        Assert.Equal(Now.AddDays(30), session.ExpiresAt);
        Assert.True(session.IsValid(Now.AddDays(29)));
        Assert.False(session.IsValid(Now.AddDays(31)));
    }

    // Touch em sessao invalida (S4)

    [Fact]
    public void REQ_012_touch_on_an_expired_session_returns_false_and_changes_nothing()
    {
        var session = AStartedSession();

        var touched = session.Touch(Now.AddMinutes(31));

        Assert.False(touched);
        Assert.Equal(Now, session.LastSeenAt);
        Assert.Equal(Now.AddMinutes(30), session.ExpiresAt);
        Assert.False(session.IsValid(Now.AddMinutes(31)));
        Assert.False(session.IsValid(Now.AddMinutes(32)));
    }

    [Fact]
    public void REQ_012_touch_at_exactly_thirty_minutes_does_not_resurrect_the_session()
    {
        var session = AStartedSession();

        var touched = session.Touch(Now.AddMinutes(30));

        Assert.False(touched);
        Assert.False(session.IsValid(Now.AddMinutes(30)));
    }

    [Fact]
    public void REQ_035_touch_on_an_expired_remember_me_session_returns_false_and_changes_nothing()
    {
        var session = AStartedSession(rememberMe: true);

        var touched = session.Touch(Now.AddDays(31));

        Assert.False(touched);
        Assert.Equal(Now, session.LastSeenAt);
        Assert.Equal(Now.AddDays(30), session.ExpiresAt);
        Assert.False(session.IsValid(Now.AddDays(31)));
    }

    [Fact]
    public void REQ_010_touch_on_a_revoked_session_returns_false_and_changes_nothing()
    {
        var session = AStartedSession();
        session.Revoke(SessionRevocationReason.Logout, Now.AddMinutes(1));

        var touched = session.Touch(Now.AddMinutes(2));

        Assert.False(touched);
        Assert.Equal(Now, session.LastSeenAt);
        Assert.Equal(Now.AddMinutes(30), session.ExpiresAt);
        Assert.False(session.IsValid(Now.AddMinutes(2)));
    }

    // Revogacao (S4, REQ-010, REQ-030)

    [Fact]
    public void REQ_010_revoking_by_logout_marks_revoked_at_and_makes_the_session_invalid()
    {
        var session = AStartedSession();
        var at = Now.AddMinutes(5);

        session.Revoke(SessionRevocationReason.Logout, at);

        Assert.Equal(at, session.RevokedAt);
        Assert.False(session.IsValid(at));
        Assert.False(session.IsValid(Now));
    }

    [Theory]
    [InlineData(SessionRevocationReason.Logout)]
    [InlineData(SessionRevocationReason.PasswordReset)]
    [InlineData(SessionRevocationReason.AccountDeletion)]
    public void REQ_030_revoking_emits_only_SessionRevoked_with_ids_and_reason(SessionRevocationReason reason)
    {
        var session = AStartedSession();

        session.Revoke(reason, Now.AddMinutes(5));

        var revoked = Assert.IsType<SessionRevoked>(Assert.Single(session.DomainEvents));
        Assert.Equal(session.Id, revoked.SessionId);
        Assert.Equal(Owner, revoked.PlayerId);
        Assert.Equal(reason, revoked.Reason);
    }

    [Fact]
    public void REQ_010_revoking_an_already_expired_session_still_marks_and_emits()
    {
        var session = AStartedSession();
        var at = Now.AddMinutes(45);

        session.Revoke(SessionRevocationReason.PasswordReset, at);

        Assert.Equal(at, session.RevokedAt);
        var revoked = Assert.IsType<SessionRevoked>(Assert.Single(session.DomainEvents));
        Assert.Equal(SessionRevocationReason.PasswordReset, revoked.Reason);
    }

    [Fact]
    public void REQ_010_revoking_again_is_idempotent_keeps_the_first_instant_and_emits_no_second_event()
    {
        var session = AStartedSession();
        var first = Now.AddMinutes(5);
        session.Revoke(SessionRevocationReason.Logout, first);

        session.Revoke(SessionRevocationReason.Logout, Now.AddMinutes(9));
        session.Revoke(SessionRevocationReason.PasswordReset, Now.AddMinutes(10));

        Assert.Equal(first, session.RevokedAt);
        var revoked = Assert.IsType<SessionRevoked>(Assert.Single(session.DomainEvents));
        Assert.Equal(SessionRevocationReason.Logout, revoked.Reason);
    }

    [Fact]
    public void REQ_011_a_revoked_session_never_becomes_valid_again()
    {
        var session = AStartedSession(rememberMe: true);
        session.Revoke(SessionRevocationReason.Logout, Now.AddMinutes(1));

        session.Touch(Now.AddMinutes(2));
        session.Revoke(SessionRevocationReason.Logout, Now.AddMinutes(3));

        Assert.False(session.IsValid(Now.AddMinutes(1)));
        Assert.False(session.IsValid(Now.AddMinutes(2)));
        Assert.False(session.IsValid(Now.AddDays(1)));
    }

    // Reautenticacao (S6, REQ-042)

    [Fact]
    public void REQ_042_a_valid_session_with_the_same_subject_is_marked_reauthenticated_now()
    {
        var session = AStartedSession();
        var at = Now.AddMinutes(3);

        var result = session.MarkReauthenticated(Google("sub-1"), Google("sub-1"), at);

        Assert.True(result.IsSuccess);
        Assert.Same(session, result.Value);
        Assert.Equal(at, session.ReauthenticatedAt);
    }

    [Fact]
    public void REQ_042_an_expired_session_gets_session_invalid_and_nothing_changes()
    {
        var session = AStartedSession();

        var result = session.MarkReauthenticated(Google("sub-1"), Google("sub-1"), Now.AddMinutes(31));

        AssertSingleError(result, "session_invalid");
        Assert.Null(session.ReauthenticatedAt);
        Assert.Equal(Now, session.LastSeenAt);
        Assert.Equal(Now.AddMinutes(30), session.ExpiresAt);
        Assert.Empty(session.DomainEvents);
    }

    [Fact]
    public void REQ_042_a_revoked_session_gets_session_invalid_and_nothing_else_changes()
    {
        var session = AStartedSession();
        session.Revoke(SessionRevocationReason.Logout, Now.AddMinutes(1));
        session.ClearDomainEvents();

        var result = session.MarkReauthenticated(Google("sub-1"), Google("sub-1"), Now.AddMinutes(2));

        AssertSingleError(result, "session_invalid");
        Assert.Null(session.ReauthenticatedAt);
        Assert.Empty(session.DomainEvents);
    }

    [Fact]
    public void REQ_042_a_different_subject_gets_subject_mismatch_and_nothing_changes()
    {
        var session = AStartedSession();

        var result = session.MarkReauthenticated(Google("sub-1"), Google("sub-2"), Now.AddMinutes(1));

        AssertSingleError(result, "subject_mismatch");
        Assert.Null(session.ReauthenticatedAt);
        Assert.Equal(Now, session.LastSeenAt);
        Assert.Equal(Now.AddMinutes(30), session.ExpiresAt);
        Assert.Empty(session.DomainEvents);
    }

    [Fact]
    public void REQ_042_session_invalid_wins_over_subject_mismatch()
    {
        var session = AStartedSession();

        var result = session.MarkReauthenticated(Google("sub-1"), Google("sub-2"), Now.AddMinutes(31));

        AssertSingleError(result, "session_invalid");
    }

    [Fact]
    public void REQ_042_a_failed_reauthentication_keeps_a_previous_timestamp()
    {
        var session = AStartedSession();
        var earlier = Now.AddMinutes(1);
        session.MarkReauthenticated(Google("sub-1"), Google("sub-1"), earlier);

        var result = session.MarkReauthenticated(Google("sub-1"), Google("sub-2"), Now.AddMinutes(2));

        AssertSingleError(result, "subject_mismatch");
        Assert.Equal(earlier, session.ReauthenticatedAt);
    }

    [Fact]
    public void REQ_042_a_new_successful_reauthentication_replaces_the_timestamp()
    {
        var session = AStartedSession();
        session.MarkReauthenticated(Google("sub-1"), Google("sub-1"), Now.AddMinutes(1));

        session.MarkReauthenticated(Google("sub-1"), Google("sub-1"), Now.AddMinutes(4));

        Assert.Equal(Now.AddMinutes(4), session.ReauthenticatedAt);
    }

    [Fact]
    public void REQ_042_to_recent_google_login_carries_the_player_and_the_reauthenticated_instant()
    {
        var session = AStartedSession();
        var at = Now.AddMinutes(3);
        session.MarkReauthenticated(Google("sub-1"), Google("sub-1"), at);

        var login = session.ToRecentGoogleLogin();

        Assert.Equal(new RecentGoogleLogin(Owner, at), login);
        Assert.Equal(Owner, login.PlayerId);
        Assert.Equal(at, login.ReauthenticatedAt);
    }

    [Fact]
    public void REQ_042_to_recent_google_login_without_reauthentication_has_a_null_instant()
    {
        var session = AStartedSession();

        var login = session.ToRecentGoogleLogin();

        Assert.Equal(Owner, login.PlayerId);
        Assert.Null(login.ReauthenticatedAt);
    }

    // Integracao com Player.Delete (T-020): a janela de 5 min e aplicada pelo Player

    [Fact]
    public void REQ_042_a_google_only_player_is_deleted_with_a_reauthentication_four_minutes_fifty_nine_old()
    {
        var player = AGoogleOnlyPlayer();
        var session = AStartedSession(player: player.Id);
        session.MarkReauthenticated(Google(), Google(), Now);

        var result = player.Delete(true, session.ToRecentGoogleLogin(), Now.AddSeconds((4 * 60) + 59));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void REQ_042_a_google_only_player_gets_reauth_expired_with_a_reauthentication_five_minutes_one_second_old()
    {
        var player = AGoogleOnlyPlayer();
        var session = AStartedSession(player: player.Id);
        session.MarkReauthenticated(Google(), Google(), Now);

        var result = player.Delete(true, session.ToRecentGoogleLogin(), Now.AddSeconds((5 * 60) + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("reauth_expired", Assert.Single(result.Errors).Code);
        Assert.Empty(player.DomainEvents);
    }

    [Fact]
    public void REQ_042_a_google_only_player_that_never_reauthenticated_gets_reauth_required()
    {
        var player = AGoogleOnlyPlayer();
        var session = AStartedSession(player: player.Id);

        var result = player.Delete(true, session.ToRecentGoogleLogin(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("reauth_required", Assert.Single(result.Errors).Code);
        Assert.Empty(player.DomainEvents);
    }
}
