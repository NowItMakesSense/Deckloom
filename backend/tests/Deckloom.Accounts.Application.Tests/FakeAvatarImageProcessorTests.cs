using Deckloom.Accounts.Application;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-028 (habilitadora; REQ-019, REQ-020, REQ-049): IAvatarImageProcessor.ProcessAsync(byte[] original, ct) devolve
// AvatarProcessingResult: AvatarAccepted(byte[] Image) ou AvatarRejected(AvatarRejectionReason Reason),
// com AvatarRejectionReason { Size, Type, Content }. O fake e programavel (AcceptWith / RejectWith), por padrao aceita
// devolvendo os bytes recebidos, e registra as entradas em Received.
public class FakeAvatarImageProcessorTests
{
    private readonly FakeAvatarImageProcessor _processor = new();

    [Fact]
    public async Task REQ019_By_default_accepts_and_returns_the_received_bytes()
    {
        byte[] original = [1, 2, 3];

        var result = await _processor.ProcessAsync(original);

        var accepted = Assert.IsType<AvatarAccepted>(result);
        Assert.Equal(original, accepted.Image);
    }

    [Fact]
    public async Task REQ019_AcceptWith_returns_the_programmed_reencoded_bytes()
    {
        _processor.AcceptWith([9, 9]);

        var result = await _processor.ProcessAsync([1, 2, 3]);

        Assert.Equal(new byte[] { 9, 9 }, Assert.IsType<AvatarAccepted>(result).Image);
    }

    [Theory]
    [InlineData(AvatarRejectionReason.Size)]
    [InlineData(AvatarRejectionReason.Type)]
    [InlineData(AvatarRejectionReason.Content)]
    public async Task REQ020_RejectWith_returns_the_programmed_reason(AvatarRejectionReason reason)
    {
        _processor.RejectWith(reason);

        var result = await _processor.ProcessAsync([1]);

        Assert.Equal(reason, Assert.IsType<AvatarRejected>(result).Reason);
    }

    [Fact]
    public async Task REQ049_The_last_programming_wins()
    {
        _processor.RejectWith(AvatarRejectionReason.Content);
        _processor.AcceptWith([5]);

        Assert.IsType<AvatarAccepted>(await _processor.ProcessAsync([1]));
    }

    [Fact]
    public async Task REQ019_Records_every_received_input_in_order()
    {
        await _processor.ProcessAsync([1]);
        await _processor.ProcessAsync([2, 2]);

        Assert.Equal(new[] { new byte[] { 1 }, [2, 2] }, _processor.Received);
    }
}
