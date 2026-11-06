using System.Net.Sockets;
using Deckloom.Accounts.Infrastructure;
using MailKit.Net.Smtp;

namespace Deckloom.Accounts.Infrastructure.Tests;

// SEC-06 (b): o transporte SMTP autentica quando "Email:Smtp:User" esta configurado. Sessao SMTP FAKE
// (ISmtpSession): nenhum teste abre conexao real.
public sealed class MailKitSmtpTransportTests
{
    private const string Password = "test-smtp-password-5b2e";

    private static readonly SmtpMessage Message = new("from@deckloom.test", "to@deckloom.test", "assunto", "corpo");

    [Fact]
    public async Task With_user_configured_it_authenticates_after_connect_and_before_sending()
    {
        var session = new FakeSession();
        var transport = new MailKitSmtpTransport("smtp.test", 587, "mailer", Password, () => session);

        await transport.SendAsync(Message, useTls: true, CancellationToken.None);

        Assert.Equal(
            ["connect:smtp.test:587:True", "auth:mailer:" + Password, "send", "disconnect"],
            session.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_user_it_sends_without_authentication(string? user)
    {
        var session = new FakeSession();
        var transport = new MailKitSmtpTransport("localhost", 1025, user, null, () => session);

        await transport.SendAsync(Message, useTls: false, CancellationToken.None);

        Assert.Equal(["connect:localhost:1025:False", "send", "disconnect"], session.Calls);
    }

    [Fact]
    public async Task Authentication_failure_never_exposes_the_password_in_the_exception()
    {
        var session = new FakeSession
        {
            AuthFailure = new InvalidOperationException($"rejeitado para a senha {Password}"),
        };
        var transport = new MailKitSmtpTransport("smtp.test", 587, "mailer", Password, () => session);

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => transport.SendAsync(Message, useTls: true, CancellationToken.None));

        Assert.DoesNotContain(Password, failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("send", session.Calls);
    }

    // T-178 (R-57): a falha vira SmtpTransportException de causa fechada, sem texto do servidor nem InnerException.
    private const string ServerText = "550 5.7.1 rejeitado: ana@leak.example.invalid https://leak.example.invalid/x#token=zzz";

    [Fact]
    public async Task R57_authentication_failure_throws_the_auth_cause_without_server_text_or_inner_exception()
    {
        var session = new FakeSession { AuthFailure = new InvalidOperationException(ServerText + Password) };
        var transport = new MailKitSmtpTransport("smtp.test", 587, "mailer", Password, () => session);

        var failure = await Assert.ThrowsAsync<SmtpTransportException>(
            () => transport.SendAsync(Message, useTls: true, CancellationToken.None));

        Assert.Equal(SmtpFailureCause.Auth, failure.Cause);
        Assert.Null(failure.InnerException);
        AssertNoLeak(failure);
    }

    [Fact]
    public async Task R57_connection_failure_throws_the_connect_cause()
    {
        var session = new FakeSession { ConnectFailure = new SocketException((int)SocketError.HostNotFound) };
        var transport = new MailKitSmtpTransport("smtp.test", 587, null, null, () => session);

        var failure = await Assert.ThrowsAsync<SmtpTransportException>(
            () => transport.SendAsync(Message, useTls: false, CancellationToken.None));

        Assert.Equal(SmtpFailureCause.Connect, failure.Cause);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task R57_tls_handshake_failure_throws_the_tls_cause()
    {
        var session = new FakeSession
        {
            ConnectFailure = new MailKit.Security.SslHandshakeException(ServerText, new InvalidOperationException(ServerText)),
        };
        var transport = new MailKitSmtpTransport("smtp.test", 587, null, null, () => session);

        var failure = await Assert.ThrowsAsync<SmtpTransportException>(
            () => transport.SendAsync(Message, useTls: true, CancellationToken.None));

        Assert.Equal(SmtpFailureCause.Tls, failure.Cause);
        Assert.Null(failure.InnerException);
        AssertNoLeak(failure);
    }

    [Fact]
    public async Task R57_authentication_exception_while_connecting_still_throws_the_tls_cause()
    {
        var session = new FakeSession
        {
            ConnectFailure = new System.Security.Authentication.AuthenticationException(ServerText),
        };
        var transport = new MailKitSmtpTransport("smtp.test", 587, null, null, () => session);

        var failure = await Assert.ThrowsAsync<SmtpTransportException>(
            () => transport.SendAsync(Message, useTls: true, CancellationToken.None));

        Assert.Equal(SmtpFailureCause.Tls, failure.Cause);
        AssertNoLeak(failure);
    }

    [Fact]
    public async Task R57_server_refusal_while_sending_throws_the_rejected_cause()
    {
        var session = new FakeSession
        {
            SendFailure = new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, ServerText),
        };
        var transport = new MailKitSmtpTransport("smtp.test", 587, null, null, () => session);

        var failure = await Assert.ThrowsAsync<SmtpTransportException>(
            () => transport.SendAsync(Message, useTls: true, CancellationToken.None));

        Assert.Equal(SmtpFailureCause.Rejected, failure.Cause);
        Assert.Null(failure.InnerException);
        AssertNoLeak(failure);
    }

    [Fact]
    public async Task R57_unclassified_failure_throws_the_other_cause()
    {
        var session = new FakeSession { SendFailure = new InvalidOperationException(ServerText) };
        var transport = new MailKitSmtpTransport("smtp.test", 587, null, null, () => session);

        var failure = await Assert.ThrowsAsync<SmtpTransportException>(
            () => transport.SendAsync(Message, useTls: true, CancellationToken.None));

        Assert.Equal(SmtpFailureCause.Other, failure.Cause);
        AssertNoLeak(failure);
    }

    [Fact]
    public async Task R57_cancellation_is_not_classified_so_the_sender_can_tell_a_timeout()
    {
        var session = new FakeSession { ConnectFailure = new OperationCanceledException() };
        var transport = new MailKitSmtpTransport("smtp.test", 587, null, null, () => session);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => transport.SendAsync(Message, useTls: true, CancellationToken.None));
    }

    private static void AssertNoLeak(Exception failure)
    {
        var text = failure.ToString();
        Assert.DoesNotContain("leak.example.invalid", text, StringComparison.Ordinal);
        Assert.DoesNotContain("token=zzz", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
    }

    private sealed class FakeSession : ISmtpSession
    {
        public List<string> Calls { get; } = [];

        public Exception? AuthFailure { get; init; }

        public Exception? ConnectFailure { get; init; }

        public Exception? SendFailure { get; init; }

        public Task ConnectAsync(string host, int port, bool useTls, CancellationToken ct)
        {
            Calls.Add($"connect:{host}:{port}:{useTls}");
            return ConnectFailure is null ? Task.CompletedTask : Task.FromException(ConnectFailure);
        }

        public Task AuthenticateAsync(string user, string password, CancellationToken ct)
        {
            Calls.Add($"auth:{user}:{password}");
            return AuthFailure is null ? Task.CompletedTask : Task.FromException(AuthFailure);
        }

        public Task SendAsync(SmtpMessage message, CancellationToken ct)
        {
            Calls.Add("send");
            return SendFailure is null ? Task.CompletedTask : Task.FromException(SendFailure);
        }

        public Task DisconnectAsync(CancellationToken ct)
        {
            Calls.Add("disconnect");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
