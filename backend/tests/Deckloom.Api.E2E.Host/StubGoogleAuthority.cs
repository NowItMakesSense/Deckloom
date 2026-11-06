using Deckloom.Accounts.Testing;

namespace Deckloom.Api.E2E.Host;

// Sobe o StubGoogleOidcServer em loopback e aponta Auth:Google para ele antes da composicao ler as opcoes.
internal sealed class StubGoogleAuthority : IDisposable
{
    private readonly StubGoogleOidcServer _server;

    private StubGoogleAuthority(StubGoogleOidcServer server) => _server = server;

    public void SetIdentity(string subject, string email, string? name, bool emailVerified, bool approve) =>
        _server.SetBrowserIdentity(subject, email, name, emailVerified, approve);

    public void UseClock(TimeProvider clock) => _server.Clock = clock;

    public static StubGoogleAuthority Start(ConfigurationManager configuration)
    {
        var server = StubGoogleOidcServer.Start();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Google:Authority"] = server.Authority.AbsoluteUri,
            ["Auth:Google:ClientId"] = server.ClientId,
            ["Auth:Google:ClientSecret"] = server.ClientSecret,
        });
        return new StubGoogleAuthority(server);
    }

    public void Dispose() => _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
