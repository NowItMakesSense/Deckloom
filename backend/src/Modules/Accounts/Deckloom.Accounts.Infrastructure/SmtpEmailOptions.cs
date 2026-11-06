namespace Deckloom.Accounts.Infrastructure;

// T-073 (REQ-028): "Email:Smtp:UseTls" e "Email:From".
public sealed class SmtpEmailOptions
{
    public string From { get; init; } = "";

    public bool UseTls { get; init; } = true;
}
