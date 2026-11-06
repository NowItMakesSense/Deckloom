using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-073 (REQ-028): SmtpPasswordResetSender com ISmtpTransport FAKE (sem SMTP real, sem Mailpit).
// Contrato esperado (a implementar na fase GREEN):
//   Infrastructure: ISmtpTransport { Task SendAsync(SmtpMessage message, bool useTls, CancellationToken ct) }
//                   SmtpMessage(From, To, Subject, Body); SmtpEmailOptions { From, UseTls }
//                   SmtpPasswordResetSender(ISmtpTransport, SmtpEmailOptions, TimeProvider, ILogger<SmtpPasswordResetSender>)
//                       Task SendAsync(PlayerId playerId, Email to, string resetLink, CancellationToken ct = default)
//                   Atraso entre tentativas via Task.Delay(delay, TimeProvider); timeout de 10 s por tentativa via
//                   CancellationTokenSource(timeout, TimeProvider). Falha final: log EventId 1008 "PasswordResetEmailFailed"
//                   com playerId e attempt, sem email/token/link, e sem excecao para o chamador.
//   Decisao pendente (ver relatorio): 3 tentativas no total => esperas de 2 s e 8 s entre elas (30 s so existiria com 4).
public sealed class SmtpPasswordResetSenderTests
{
    private const string BaseUrl = "https://deckloom.example";
    private const string Token = "tok-abc123";
    private const string From = "nao-responda@deckloom.example";
    private const string Recipient = "ana@example.com";

    private static readonly string Link = $"{BaseUrl}/redefinir-senha#token={Token}";
    private static readonly PlayerId Player = new(Guid.Parse("11111111-2222-3333-4444-555555555555"));

    private readonly FakeTimeProvider _time = new();
    private readonly FakeSmtpTransport _transport = new();
    private readonly SpyLogger _log = new();

    private static Email Ana => Email.Create(Recipient).Value;

    private SmtpPasswordResetSender NewSender(bool useTls = true) =>
        new(_transport, new SmtpEmailOptions { From = From, UseTls = useTls }, _time, _log);

    [Fact]
    public async Task REQ_028_message_is_in_portuguese_with_token_in_fragment_and_never_in_query()
    {
        await NewSender().SendAsync(Player, Ana, Link);

        var message = Assert.Single(_transport.Messages);
        Assert.Equal(Recipient, message.To);
        Assert.Contains(Link, message.Body);
        Assert.DoesNotContain("?token", message.Body);
        Assert.DoesNotContain("?token", message.Subject);
        Assert.Contains("redefin", message.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("senha", message.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task REQ_028_sender_address_is_the_configured_email_from()
    {
        await NewSender().SendAsync(Player, Ana, Link);

        Assert.Equal(From, Assert.Single(_transport.Messages).From);
    }

    [Fact]
    public async Task REQ_028_retries_up_to_three_attempts_with_2s_then_8s_backoff_on_fake_clock()
    {
        _transport.FailAlways();
        var sending = NewSender().SendAsync(Player, Ana, Link);

        await WaitForCallsAsync(1);
        _time.Advance(TimeSpan.FromSeconds(1.9));
        await Settle();
        Assert.Equal(1, _transport.Calls);

        _time.Advance(TimeSpan.FromSeconds(0.1));
        await WaitForCallsAsync(2);
        _time.Advance(TimeSpan.FromSeconds(7.9));
        await Settle();
        Assert.Equal(2, _transport.Calls);

        _time.Advance(TimeSpan.FromSeconds(0.1));
        await WaitForCallsAsync(3);
        await sending;
        Assert.Equal(3, _transport.Calls);
    }

    [Fact]
    public async Task REQ_028_succeeds_on_a_retry_without_logging_failure()
    {
        _transport.FailTimes(1);
        var sending = NewSender().SendAsync(Player, Ana, Link);

        await WaitForCallsAsync(1);
        _time.Advance(TimeSpan.FromSeconds(2));
        await sending;

        Assert.Equal(2, _transport.Calls);
        Assert.DoesNotContain(_log.Entries, e => e.EventId.Id == 1008);
    }

    [Fact]
    public async Task REQ_028_final_failure_logs_event_1008_with_player_and_attempt_only_and_does_not_throw()
    {
        _transport.FailAlways();
        var sending = NewSender().SendAsync(Player, Ana, Link);

        await WaitForCallsAsync(1);
        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitForCallsAsync(2);
        _time.Advance(TimeSpan.FromSeconds(8));
        await WaitForCallsAsync(3);
        await sending;

        var entry = Assert.Single(_log.Entries, e => e.EventId.Id == 1008);
        Assert.Equal("PasswordResetEmailFailed", entry.EventId.Name);
        Assert.Contains(entry.Pairs, p => p.Key == "playerId" && Equals(p.Value?.ToString(), Player.Value.ToString()));
        Assert.Contains(entry.Pairs, p => p.Key == "attempt" && Equals(p.Value?.ToString(), "3"));

        var everything = string.Join("|", _log.Entries.Select(e => e.Text));
        Assert.DoesNotContain(Recipient, everything);
        Assert.DoesNotContain("example.com", everything);
        Assert.DoesNotContain(Token, everything);
        Assert.DoesNotContain("redefinir-senha", everything);
    }

    [Fact]
    public async Task REQ_028_each_attempt_times_out_after_10_seconds_and_then_backs_off()
    {
        _transport.HangUntilCancelled();
        _ = NewSender().SendAsync(Player, Ana, Link);

        await WaitForCallsAsync(1);
        _time.Advance(TimeSpan.FromSeconds(9.9));
        await Settle();
        Assert.False(_transport.LastToken.IsCancellationRequested);

        _time.Advance(TimeSpan.FromSeconds(0.1));
        await Settle();
        Assert.True(_transport.LastToken.IsCancellationRequested);

        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitForCallsAsync(2);
        Assert.False(_transport.LastToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task REQ_028_transport_connects_with_tls_exactly_as_configured(bool useTls)
    {
        await NewSender(useTls).SendAsync(Player, Ana, Link);

        Assert.Equal(useTls, Assert.Single(_transport.TlsFlags));
    }

    // T-178 (R-57): o evento 1008 traz `cause` de lista fechada. Cada tipo de falha do transporte vira uma causa.
    public static TheoryData<SmtpFailureCause, string> TransportFailureCauses => new()
    {
        { SmtpFailureCause.Connect, "connect" },
        { SmtpFailureCause.Tls, "tls" },
        { SmtpFailureCause.Auth, "auth" },
        { SmtpFailureCause.Rejected, "rejected" },
        { SmtpFailureCause.Other, "other" },
    };

    [Theory]
    [MemberData(nameof(TransportFailureCauses))]
    public async Task R57_final_failure_logs_the_cause_of_the_transport_failure(SmtpFailureCause failure, string expected)
    {
        _transport.FailAlwaysWith(new SmtpTransportException(failure));

        await SendWithAllAttemptsFailingAsync();

        Assert.Equal(expected, CauseOf(Assert.Single(_log.Entries, e => e.EventId.Id == 1008)));
    }

    [Fact]
    public async Task R57_an_unclassified_exception_logs_cause_other()
    {
        _transport.FailAlwaysWith(new InvalidOperationException("qualquer coisa"));

        await SendWithAllAttemptsFailingAsync();

        Assert.Equal("other", CauseOf(Assert.Single(_log.Entries, e => e.EventId.Id == 1008)));
    }

    [Fact]
    public async Task R57_attempts_that_hit_the_10s_timeout_log_cause_timeout()
    {
        _transport.HangUntilCancelled();
        var sending = NewSender().SendAsync(Player, Ana, Link);

        await WaitForCallsAsync(1);
        _time.Advance(TimeSpan.FromSeconds(10));
        await Settle();
        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitForCallsAsync(2);
        _time.Advance(TimeSpan.FromSeconds(10));
        await Settle();
        _time.Advance(TimeSpan.FromSeconds(8));
        await WaitForCallsAsync(3);
        _time.Advance(TimeSpan.FromSeconds(10));
        await sending;

        Assert.Equal("timeout", CauseOf(Assert.Single(_log.Entries, e => e.EventId.Id == 1008)));
    }

    public static TheoryData<SmtpFailureCause> AllCauses()
    {
        var data = new TheoryData<SmtpFailureCause>();
        foreach (var cause in Enum.GetValues<SmtpFailureCause>())
        {
            data.Add(cause);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllCauses))]
    public async Task R57_cause_is_always_one_of_the_closed_list(SmtpFailureCause failure)
    {
        _transport.FailAlwaysWith(new SmtpTransportException(failure));

        await SendWithAllAttemptsFailingAsync();

        var cause = CauseOf(Assert.Single(_log.Entries, e => e.EventId.Id == 1008));
        Assert.Contains(cause, ClosedCauses);
    }

    // Os valores abaixo sao sinteticos: o teste nao le configuracao nem segredo real.
    [Fact]
    public async Task R57_log_never_carries_secret_email_or_link_from_the_exception_message_or_inner_exception()
    {
        const string leakedSecret = "synthetic-smtp-secret-7e41";
        const string leakedRecipient = "leak.target@leak.example.invalid";
        const string leakedLink = "https://leak.example.invalid/redefinir-senha#token=synthetic-token-9a3c";
        var leaked = $"servidor recusou {leakedSecret} para {leakedRecipient} em {leakedLink}";
        _transport.FailAlwaysWith(new InvalidOperationException(leaked, new IOException(leaked)));

        await SendWithAllAttemptsFailingAsync();

        var entry = Assert.Single(_log.Entries, e => e.EventId.Id == 1008);
        Assert.Null(entry.Exception);
        var everything = string.Join("|", _log.Entries.Select(e => e.Text));
        foreach (var leak in new[] { leakedSecret, leakedRecipient, leakedLink, "synthetic-token-9a3c", "leak.example.invalid" })
        {
            Assert.DoesNotContain(leak, everything, StringComparison.Ordinal);
            Assert.DoesNotContain(
                entry.Pairs,
                p => (p.Value?.ToString() ?? string.Empty).Contains(leak, StringComparison.Ordinal));
        }

        Assert.Equal("other", CauseOf(entry));
        Assert.All(_log.Entries, e => Assert.Null(e.Exception));
    }

    private static readonly string[] ClosedCauses = ["connect", "tls", "auth", "rejected", "timeout", "other"];

    private static string? CauseOf(LogEntry entry) => entry.Pairs.SingleOrDefault(p => p.Key == "cause").Value?.ToString();

    private async Task SendWithAllAttemptsFailingAsync()
    {
        var sending = NewSender().SendAsync(Player, Ana, Link);
        await WaitForCallsAsync(1);
        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitForCallsAsync(2);
        _time.Advance(TimeSpan.FromSeconds(8));
        await WaitForCallsAsync(3);
        await sending;
    }

    private async Task WaitForCallsAsync(int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_transport.Calls < expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(1);
        }

        Assert.True(_transport.Calls >= expected, $"esperava {expected} chamadas ao transporte, houve {_transport.Calls}");
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(1);
        }
    }

    private sealed class FakeSmtpTransport : ISmtpTransport
    {
        private int _failuresLeft;
        private bool _hang;

        public List<SmtpMessage> Messages { get; } = [];

        public List<bool> TlsFlags { get; } = [];

        public int Calls { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public Exception? Failure { get; private set; }

        public void FailAlways() => _failuresLeft = int.MaxValue;

        public void FailAlwaysWith(Exception failure)
        {
            Failure = failure;
            _failuresLeft = int.MaxValue;
        }

        public void FailTimes(int times) => _failuresLeft = times;

        public void HangUntilCancelled() => _hang = true;

        public async Task SendAsync(SmtpMessage message, bool useTls, CancellationToken ct)
        {
            Calls++;
            LastToken = ct;
            if (_hang)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            if (_failuresLeft > 0)
            {
                _failuresLeft--;
                throw Failure ?? new InvalidOperationException("falha SMTP simulada");
            }

            Messages.Add(message);
            TlsFlags.Add(useTls);
        }
    }

    private sealed record LogEntry(
        EventId EventId, string Text, IReadOnlyList<KeyValuePair<string, object?>> Pairs, Exception? Exception);

    private sealed class SpyLogger : ILogger<SmtpPasswordResetSender>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var pairs = state is IEnumerable<KeyValuePair<string, object?>> kv ? kv.ToList() : [];
            var text = formatter(state, exception) + " " + exception + " " + string.Join(" ", pairs.Select(p => $"{p.Key}={p.Value}"));
            Entries.Add(new LogEntry(eventId, text, pairs, exception));
        }
    }
}
