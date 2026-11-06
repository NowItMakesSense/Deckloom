namespace Deckloom.Accounts.Application;

public interface IAvatarStorage
{
    Task SaveAsync(string key, byte[] image, CancellationToken ct = default);

    Task<byte[]?> ReadAsync(string key, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);
}
