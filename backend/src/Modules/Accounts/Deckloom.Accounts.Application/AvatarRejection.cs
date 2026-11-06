using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

// Traduz o motivo de rejeicao da imagem no erro de dominio `avatar_rejected`; o campo carrega o `reason` (size|type|content).
internal static class AvatarRejection
{
    public const string Code = "avatar_rejected";

    public static DomainError ToError(AvatarRejectionReason reason) => new(Code, CodeOf(reason));

    // Conta a rejeicao e devolve o erro de dominio correspondente.
    public static DomainError Reject(AvatarRejectionReason reason)
    {
        var code = CodeOf(reason);
        AccountsMetrics.AvatarRejected(code);
        return new DomainError(Code, code);
    }

    private static string CodeOf(AvatarRejectionReason reason) => reason switch
    {
        AvatarRejectionReason.Size => "size",
        AvatarRejectionReason.Type => "type",
        AvatarRejectionReason.Content => "content",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
