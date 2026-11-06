using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

public sealed class RecordingPasswordResetNotifier : IPasswordResetNotifier
{
    private readonly List<SentResetLink> _sent = [];
    private Exception? _failure;

    public TransactionProbe TransactionProbe { get; } = new();

    public IReadOnlyList<SentResetLink> Sent => _sent;

    public void FailWith(Exception exception) => _failure = exception;

    public Task NotifyAsync(Email email, string resetLink, CancellationToken ct = default) =>
        NotifyAsync(default, email, resetLink, ct);

    public Task NotifyAsync(PlayerId playerId, Email email, string resetLink, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        TransactionProbe.Record("Notify");
        if (_failure is not null)
        {
            throw _failure;
        }

        _sent.Add(new SentResetLink(email, resetLink, playerId));
        return Task.CompletedTask;
    }
}

public sealed record SentResetLink(Email Email, string Link, PlayerId PlayerId = default);
