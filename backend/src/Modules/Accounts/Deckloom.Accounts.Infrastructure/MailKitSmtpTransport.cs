using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Deckloom.Accounts.Infrastructure;

// T-073 (REQ-028): transporte real sobre MailKit (ADR-0010). Servidor em "Email:Smtp:Host"/"Port".
// SEC-06 (b): com "Email:Smtp:User" preenchido, autentica antes de enviar; vazio, envia sem autenticar (Mailpit local).
public sealed class MailKitSmtpTransport : ISmtpTransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly string? _user;
    private readonly string? _password;
    private readonly Func<ISmtpSession> _sessionFactory;

    public MailKitSmtpTransport(
        string host, int port, string? user = null, string? password = null, Func<ISmtpSession>? sessionFactory = null)
    {
        _host = host;
        _port = port;
        _user = user;
        _password = password;
        _sessionFactory = sessionFactory ?? (() => new MailKitSession());
    }

    public async Task SendAsync(SmtpMessage message, bool useTls, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var session = _sessionFactory();
        await StepAsync(SmtpFailureCause.Connect, () => session.ConnectAsync(_host, _port, useTls, ct));
        if (!string.IsNullOrWhiteSpace(_user))
        {
            await StepAsync(SmtpFailureCause.Auth, () => session.AuthenticateAsync(_user, _password ?? "", ct));
        }

        await StepAsync(SmtpFailureCause.Other, () => session.SendAsync(message, ct));
        await StepAsync(SmtpFailureCause.Other, () => session.DisconnectAsync(ct));
    }

    // R-57: unico ponto de classificacao. A excecao original nao e propagada (sem InnerException nem texto do
    // servidor): a mensagem da biblioteca pode carregar senha, e-mail ou link. Cancelamento passa sem classificar.
    private static async Task StepAsync(SmtpFailureCause stageCause, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A falha vira SmtpTransportException de causa fechada; nada do original propaga.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            throw new SmtpTransportException(Classify(stageCause, ex));
        }
    }

    private static SmtpFailureCause Classify(SmtpFailureCause stageCause, Exception ex) => stageCause switch
    {
        // MailKit lanca SslHandshakeException na negociacao TLS; AuthenticationException fica por defesa (R-67).
        SmtpFailureCause.Connect when ex is SslHandshakeException or System.Security.Authentication.AuthenticationException
            => SmtpFailureCause.Tls,
        SmtpFailureCause.Other when ex is SmtpCommandException => SmtpFailureCause.Rejected,
        _ => stageCause,
    };

    private sealed class MailKitSession : ISmtpSession
    {
        private readonly SmtpClient _client = new();

        public Task ConnectAsync(string host, int port, bool useTls, CancellationToken ct) =>
            _client.ConnectAsync(host, port, useTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);

        public Task AuthenticateAsync(string user, string password, CancellationToken ct) =>
            _client.AuthenticateAsync(user, password, ct);

        public async Task SendAsync(SmtpMessage message, CancellationToken ct)
        {
            using var mime = new MimeMessage();
            mime.From.Add(MailboxAddress.Parse(message.From));
            mime.To.Add(MailboxAddress.Parse(message.To));
            mime.Subject = message.Subject;
            mime.Body = new TextPart("plain") { Text = message.Body };
            await _client.SendAsync(mime, ct);
        }

        public Task DisconnectAsync(CancellationToken ct) => _client.DisconnectAsync(true, ct);

        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
