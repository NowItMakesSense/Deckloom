using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-079 (habilitadora; NFR de observabilidade e logs sem PII). Catalogo de design.md: 1001-1022
// (1021 DatabaseCommandFailed, ADR-0015, estendido por T-139); 1022 AccessBudgetExceeded (SEC-01, ADR-0005 E9).
// Redacao de PII em runtime e do T-093 (fora daqui).
public sealed class LogCatalogTests
{
    public static TheoryData<int, string, string> ExpectedEvents => new()
    {
        { 1001, "PlayerRegistered", "playerId,method" },
        { 1002, "LoginSucceeded", "playerId,sessionId,rememberMe" },
        { 1003, "LoginFailed", "reason,playerId" },
        { 1004, "LoggedOut", "playerId,sessionId" },
        { 1005, "PasswordResetRequested", "outcome,playerId" },
        { 1006, "PasswordResetCompleted", "playerId,revokedSessions" },
        { 1007, "PasswordResetRejected", "reason" },
        { 1008, "PasswordResetEmailFailed", "playerId,attempt,cause" },
        { 1009, "GoogleAuthCompleted", "outcome,playerId" },
        { 1010, "GoogleAuthFailed", "stage,reason" },
        { 1011, "GoogleLinked", "playerId" },
        { 1012, "GoogleLinkRejected", "playerId,reason" },
        { 1013, "ProfileUpdated", "playerId,changedFields" },
        { 1014, "AvatarChanged", "playerId,kind" },
        { 1015, "AvatarRejected", "playerId,reason,detail" },
        { 1016, "ProgressiveDelayApplied", "action,delayMs,scope" },
        { 1017, "AccountDeleted", "playerId" },
        { 1018, "AccountDeletionRejected", "playerId,reason" },
        { 1019, "SessionRevoked", "playerId,sessionId,reason" },
        { 1020, "CsrfRejected", "path" },
        { 1021, "DatabaseCommandFailed", "sqlState,operation" },
        { 1022, "AccessBudgetExceeded", "action" },
    };

    private static readonly string[] ForbiddenFieldTerms =
    [
        "email", "token", "link", "password", "senha", "name", "ip", "address", "cookie", "body", "url",
    ];

    [Fact]
    public void Catalog_contains_exactly_events_1001_to_1022_in_order()
    {
        Assert.Equal(Enumerable.Range(1001, 22), AccountsLog.Catalog.Select(e => e.Id));
    }

    [Theory]
    [MemberData(nameof(ExpectedEvents))]
    public void Event_has_stable_name_and_only_the_allowed_fields(int id, string name, string fields)
    {
        var entry = Assert.Single(AccountsLog.Catalog, e => e.Id == id);

        Assert.Equal(name, entry.Name);
        Assert.Equal(fields.Split(','), entry.Fields);
    }

    [Fact]
    public void Event_names_are_unique()
    {
        var names = AccountsLog.Catalog.Select(e => e.Name).ToList();

        Assert.NotEmpty(names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void No_event_declares_a_pii_field()
    {
        Assert.NotEmpty(AccountsLog.Catalog);

        var offenders = AccountsLog.Catalog
            .SelectMany(e => e.Fields.Select(f => (e.Id, Field: f)))
            .Where(x => IsPii(x.Field))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Pii_scan_rejects_known_pii_field_names()
    {
        Assert.True(IsPii("email"));
        Assert.True(IsPii("resetToken"));
        Assert.True(IsPii("resetLink"));
        Assert.True(IsPii("password"));
        Assert.True(IsPii("displayName"));
        Assert.True(IsPii("clientIp"));
        Assert.False(IsPii("playerId"));
        Assert.False(IsPii("path"));
    }

    [Fact]
    public void Recording_provider_captures_event_id_level_and_structured_properties()
    {
        using var provider = new RecordingLoggerProvider();
        var logger = provider.CreateLogger("test");

        logger.Log(
            LogLevel.Error,
            new EventId(1008, "PasswordResetEmailFailed"),
            new List<KeyValuePair<string, object?>> { new("playerId", Guid.Empty), new("attempt", 3) },
            null,
            (_, _) => "falhou");

        var recorded = Assert.Single(provider.Events);
        Assert.Equal(1008, recorded.EventId.Id);
        Assert.Equal("PasswordResetEmailFailed", recorded.EventId.Name);
        Assert.Equal(LogLevel.Error, recorded.Level);
        Assert.Equal(3, recorded.Properties["attempt"]);
        Assert.Equal(Guid.Empty, recorded.Properties["playerId"]);
    }

    private static bool IsPii(string field)
    {
        var words = Regex.Split(field, "(?<=[a-z0-9])(?=[A-Z])|[_\\-]");
        return words.Any(w => ForbiddenFieldTerms.Contains(w, StringComparer.OrdinalIgnoreCase));
    }
}
