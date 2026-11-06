using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application.Tests;

internal static class PlayerFixtures
{
    private const string Terms = "2026-10-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    public static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    public static Email AnEmail(string value = "ana@example.com") => Email.Create(value).Value;

    public static Player APasswordPlayer(string email = "ana@example.com") =>
        Player.RegisterWithPassword(
            AnEmail(email),
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create("Ana").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-01").Value;

    public static Player AGooglePlayer(string email, string subject) =>
        Player.RegisterWithGoogle(
            AnEmail(email),
            ExternalIdentity.Create("google", subject).Value,
            DisplayName.Create("Gui").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-01").Value;

    public static TokenHash AHash(byte seed) =>
        TokenHash.Create(Enumerable.Repeat(seed, TokenHash.Length).ToArray()).Value;

    public static Session ASession(PlayerId playerId, byte seed, bool rememberMe = false, DateTimeOffset? at = null) =>
        Session.Start(playerId, AHash(seed), rememberMe, at ?? Now);
}
