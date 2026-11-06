namespace Deckloom.Accounts.Infrastructure;

// SEC-06 (b): costura sobre a sessao SMTP para o transporte ser testavel sem conexao real.
public interface ISmtpSession : IAsyncDisposable
{
    Task ConnectAsync(string host, int port, bool useTls, CancellationToken ct);

    Task AuthenticateAsync(string user, string password, CancellationToken ct);

    Task SendAsync(SmtpMessage message, CancellationToken ct);

    Task DisconnectAsync(CancellationToken ct);
}
