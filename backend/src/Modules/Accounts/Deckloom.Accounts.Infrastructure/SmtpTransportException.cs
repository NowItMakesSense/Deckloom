namespace Deckloom.Accounts.Infrastructure;

/// <summary>Causa de falha do envio SMTP em lista fechada (R-57); vira o campo cause do evento 1008.</summary>
public enum SmtpFailureCause
{
    Connect,
    Tls,
    Auth,
    Rejected,
    Timeout,
    Other,
}

// Falha do transporte SMTP com causa fechada (T-178). Nao carrega mensagem do servidor nem InnerException.
public sealed class SmtpTransportException : Exception
{
    public SmtpTransportException(SmtpFailureCause cause)
        : base("Falha no envio SMTP.")
    {
        Cause = cause;
    }

    public SmtpFailureCause Cause { get; }
}
