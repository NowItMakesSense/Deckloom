using System.ComponentModel.DataAnnotations;

namespace Deckloom.Api.Http;

// Limitador de concorrencia por IP nos endpoints de autenticacao (REQ-032). Configuracao: Http:ConcurrencyLimiter.
public sealed class AuthConcurrencyOptions
{
    public const string SectionName = "Http:ConcurrencyLimiter";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 4;

    [Range(1, int.MaxValue)]
    public int QueueLimit { get; set; } = 16;
}
