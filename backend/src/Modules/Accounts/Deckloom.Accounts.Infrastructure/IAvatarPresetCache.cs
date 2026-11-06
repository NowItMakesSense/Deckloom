namespace Deckloom.Accounts.Infrastructure;

// Fonte do catalogo de presets ativos ja carregado (REQ-018). Os tres metodos do catalogo leem daqui.
internal interface IAvatarPresetCache
{
    Task<IReadOnlyList<AvatarPresetRow>> GetActiveAsync(CancellationToken ct);
}
