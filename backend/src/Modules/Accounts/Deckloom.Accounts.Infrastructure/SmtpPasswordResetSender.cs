using Deckloom.Accounts.Domain;
using Microsoft.Extensions.Logging;

namespace Deckloom.Accounts.Infrastructure;

// T-073 (REQ-028): envio do e-mail de redefinicao de senha por SMTP, com 3 tentativas no total
// (esperas de 2 s e 8 s) e timeout de 10 s por tentativa. Falha final so e logada, sem dados pessoais.
public sealed partial class SmtpPasswordResetSender : IPasswordResetSender
{
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan[] BackoffBetweenAttempts = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)];

    private readonly ISmtpTransport _transport;
    private readonly SmtpEmailOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<SmtpPasswordResetSender> _logger;

    public SmtpPasswordResetSender(
        ISmtpTransport transport,
        SmtpEmailOptions options,
        TimeProvider time,
        ILogger<SmtpPasswordResetSender> logger)
    {
        _transport = transport;
        _options = options;
        _time = time;
        _logger = logger;
    }

    public async Task SendAsync(PlayerId playerId, Email recipient, string resetLink, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(resetLink);

        var message = new SmtpMessage(
            _options.From,
            recipient.Value,
            PasswordResetEmailTemplate.Subject,
            PasswordResetEmailTemplate.RenderBody(resetLink));

        var attempts = BackoffBetweenAttempts.Length + 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var cause = await TrySendOnceAsync(message, ct);
            if (cause is null)
            {
                return;
            }

            if (attempt == attempts)
            {
                AccountsLog.PasswordResetEmailFailed(_logger, LogLevel.Error, playerId.Value, attempt, CauseName(cause.Value));
                return;
            }

            await Task.Delay(BackoffBetweenAttempts[attempt - 1], _time, ct);
        }
    }

    private static string CauseName(SmtpFailureCause cause) => cause.ToString().ToLowerInvariant();

    // Devolve null no sucesso, ou a causa da falha. Nunca expoe a excecao: so a causa fechada vai ao log (R-57).
    private async Task<SmtpFailureCause?> TrySendOnceAsync(SmtpMessage message, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(AttemptTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await _transport.SendAsync(message, _options.UseTls, linked.Token);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return SmtpFailureCause.Timeout;
        }
        catch (SmtpTransportException ex)
        {
            return ex.Cause;
        }
#pragma warning disable CA1031 // Qualquer falha do transporte vira nova tentativa; nada propaga ao chamador.
        catch (Exception)
#pragma warning restore CA1031
        {
            return SmtpFailureCause.Other;
        }
    }
}
