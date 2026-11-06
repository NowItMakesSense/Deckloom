using System.ComponentModel.DataAnnotations;

namespace Deckloom.Api.Http;

// Limites de corpo das requisicoes (REQ-032, ADR-0005 item 5). Configuracao: Http:Limits.
public sealed class HttpLimitsOptions
{
    public const string SectionName = "Http:Limits";

    [Range(1, int.MaxValue)]
    public long MaxJsonBodyBytes { get; set; } = 16 * 1024;

    [Range(1, int.MaxValue)]
    public long MaxUploadBytes { get; set; } = 2_621_440;
}
