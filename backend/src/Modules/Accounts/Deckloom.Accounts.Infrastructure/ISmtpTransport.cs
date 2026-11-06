namespace Deckloom.Accounts.Infrastructure;

// T-073 (REQ-028): stub da fase RED; o wrapper MailKit vem na fase GREEN.
public sealed record SmtpMessage(string From, string To, string Subject, string Body);

public interface ISmtpTransport
{
    Task SendAsync(SmtpMessage message, bool useTls, CancellationToken ct);
}
