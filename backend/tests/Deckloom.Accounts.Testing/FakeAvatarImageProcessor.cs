using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Testing;

public sealed class FakeAvatarImageProcessor : IAvatarImageProcessor
{
    private readonly List<byte[]> _received = [];
    private Func<byte[], AvatarProcessingResult> _response = original => new AvatarAccepted(original);

    public TransactionProbe TransactionProbe { get; } = new();

    public IReadOnlyList<byte[]> Received => _received;

    public void AcceptWith(byte[] image) => _response = _ => new AvatarAccepted(image);

    public void RejectWith(AvatarRejectionReason reason) => _response = _ => new AvatarRejected(reason);

    public Task<AvatarProcessingResult> ProcessAsync(byte[] original, CancellationToken ct = default)
    {
        TransactionProbe.Record("Process");
        _received.Add(original);
        return Task.FromResult(_response(original));
    }
}
