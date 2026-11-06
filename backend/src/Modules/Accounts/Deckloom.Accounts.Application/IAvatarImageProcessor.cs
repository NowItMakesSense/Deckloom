namespace Deckloom.Accounts.Application;

public interface IAvatarImageProcessor
{
    Task<AvatarProcessingResult> ProcessAsync(byte[] original, CancellationToken ct = default);
}

public abstract record AvatarProcessingResult;

public sealed record AvatarAccepted(byte[] Image) : AvatarProcessingResult;

public sealed record AvatarRejected(AvatarRejectionReason Reason) : AvatarProcessingResult;

public enum AvatarRejectionReason
{
    Size,
    Type,
    Content,
}
