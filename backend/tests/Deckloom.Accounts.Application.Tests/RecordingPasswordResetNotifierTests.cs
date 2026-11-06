using Deckloom.Accounts.Application;
using Deckloom.Accounts.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-028 (habilitadora; REQ-028): IPasswordResetNotifier.NotifyAsync(Email, string resetLink, ct) e o fake que grava
// (email, link) em Sent, na ordem das chamadas, e permite falha injetada com FailWith(Exception).
public class RecordingPasswordResetNotifierTests
{
    private readonly RecordingPasswordResetNotifier _notifier = new();

    [Fact]
    public void REQ028_Starts_with_nothing_sent()
    {
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public async Task REQ028_Records_the_email_and_the_link()
    {
        await _notifier.NotifyAsync(AnEmail("ana@example.com"), "https://app.test/reset#token=tok-0001");

        var sent = Assert.Single(_notifier.Sent);
        Assert.Equal(AnEmail("ana@example.com"), sent.Email);
        Assert.Equal("https://app.test/reset#token=tok-0001", sent.Link);
    }

    [Fact]
    public async Task REQ028_Records_every_call_in_order()
    {
        await _notifier.NotifyAsync(AnEmail("ana@example.com"), "link-1");
        await _notifier.NotifyAsync(AnEmail("gui@example.com"), "link-2");

        Assert.Equal(["link-1", "link-2"], _notifier.Sent.Select(s => s.Link));
    }

    [Fact]
    public async Task REQ028_FailWith_makes_NotifyAsync_throw_the_injected_exception_and_record_nothing()
    {
        var boom = new InvalidOperationException("smtp down");
        _notifier.FailWith(boom);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _notifier.NotifyAsync(AnEmail(), "link"));

        Assert.Same(boom, thrown);
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public async Task REQ028_NotifyAsync_with_a_cancelled_token_throws_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _notifier.NotifyAsync(AnEmail(), "link", cts.Token));
        Assert.Empty(_notifier.Sent);
    }
}
