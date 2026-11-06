using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed class GetPendingGoogleAuth(IPendingGoogleAuthStore store, ITokenHasher tokenHasher)
{
    private static readonly DomainError NotFound = new("pending_not_found");

    public async Task<Result<PendingGoogleAuthView>> ExecuteAsync(
        GetPendingGoogleAuthCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.PendingToken))
        {
            return Result<PendingGoogleAuthView>.Failure(NotFound);
        }

        var pending = await store.FindAsync(tokenHasher.Hash(command.PendingToken), ct);
        return pending switch
        {
            PendingGoogleSignup signup => Result<PendingGoogleAuthView>.Success(
                new PendingGoogleAuthView(PendingGoogleAuthKind.Signup, signup.Email.Value, signup.SuggestedDisplayName)),
            PendingGoogleLink link => Result<PendingGoogleAuthView>.Success(
                new PendingGoogleAuthView(PendingGoogleAuthKind.Link, link.Email.Value, null)),
            _ => Result<PendingGoogleAuthView>.Failure(NotFound),
        };
    }
}
