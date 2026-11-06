using System.Reflection;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-074 (REQ-028): fila em memoria do notificador + BackgroundService de envio. Sender FAKE, sem SMTP, sem banco.
// Contrato esperado (a implementar na fase GREEN):
//   ChannelPasswordResetNotifier(int capacity, ILogger<ChannelPasswordResetNotifier>) : IPasswordResetNotifier
//       NotifyAsync devolve Task ja concluida (nunca espera o envio); cheia => descarta e loga sem dados pessoais.
//       Reader: ChannelReader<PasswordResetEmail>; PasswordResetEmail(PlayerId, Email, string ResetLink).
//   PasswordResetEmailWorker(ChannelPasswordResetNotifier, IPasswordResetSender, ILogger<PasswordResetEmailWorker>) : BackgroundService
//   IPasswordResetSender (implementada por SmtpPasswordResetSender).
public sealed class PasswordResetQueueTests
{
    private const string Token = "tok-abc123xyz";
    private const string Recipient = "ana@example.com";
    private const string OtherRecipient = "outra@example.com";
    private static readonly string Link = $"https://deckloom.example/redefinir-senha#token={Token}";
    private static readonly PlayerId Player = new(Guid.Parse("11111111-2222-3333-4444-555555555555"));
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static Email Ana => Email.Create(Recipient).Value;

    private static Email Outra => Email.Create(OtherRecipient).Value;

    [Fact]
    public async Task REQ_028_enqueue_returns_immediately_without_waiting_for_the_send_whether_or_not_the_account_exists()
    {
        var queue = new ChannelPasswordResetNotifier(10, new SpyLogger<ChannelPasswordResetNotifier>());
        var blockedSender = new FakeSender { Gate = new TaskCompletionSource() };
        using var worker = new PasswordResetEmailWorker(queue, blockedSender, new SpyLogger<PasswordResetEmailWorker>());
        await worker.StartAsync(CancellationToken.None);

        var existing = queue.NotifyAsync(Player, Ana, Link);
        var other = queue.NotifyAsync(new PlayerId(Guid.NewGuid()), Outra, Link);

        Assert.True(existing.IsCompletedSuccessfully);
        Assert.True(other.IsCompletedSuccessfully);

        blockedSender.Gate!.SetResult();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task REQ_028_worker_consumes_the_queue_and_calls_the_sender()
    {
        var queue = new ChannelPasswordResetNotifier(10, new SpyLogger<ChannelPasswordResetNotifier>());
        var sender = new FakeSender();
        using var worker = new PasswordResetEmailWorker(queue, sender, new SpyLogger<PasswordResetEmailWorker>());
        await worker.StartAsync(CancellationToken.None);

        await queue.NotifyAsync(Player, Ana, Link);

        await sender.WaitForCallsAsync(1);
        var call = Assert.Single(sender.Calls);
        Assert.Equal(Player, call.PlayerId);
        Assert.Equal(Recipient, call.To.Value);
        Assert.Equal(Link, call.ResetLink);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task REQ_028_over_capacity_drops_and_logs_without_personal_data()
    {
        var log = new SpyLogger<ChannelPasswordResetNotifier>();
        var queue = new ChannelPasswordResetNotifier(2, log);

        for (var i = 0; i < 3; i++)
        {
            await queue.NotifyAsync(Player, Ana, Link);
        }

        Assert.True(queue.Reader.TryRead(out _));
        Assert.True(queue.Reader.TryRead(out _));
        Assert.False(queue.Reader.TryRead(out _));
        var entry = Assert.Single(log.Entries);
        Assert.True(entry.Level >= LogLevel.Warning);
        AssertNoPersonalData(entry.Text);
    }

    [Fact]
    public async Task REQ_028_sender_failure_does_not_stop_the_worker_nor_reveal_the_account()
    {
        var queue = new ChannelPasswordResetNotifier(10, new SpyLogger<ChannelPasswordResetNotifier>());
        var sender = new FakeSender { FailFirst = true };
        var workerLog = new SpyLogger<PasswordResetEmailWorker>();
        using var worker = new PasswordResetEmailWorker(queue, sender, workerLog);
        await worker.StartAsync(CancellationToken.None);

        await queue.NotifyAsync(Player, Ana, Link);
        await queue.NotifyAsync(new PlayerId(Guid.NewGuid()), Outra, Link);

        await sender.WaitForCallsAsync(2);
        Assert.Equal(2, sender.Calls.Count);
        Assert.False(worker.ExecuteTask?.IsCompleted ?? true);
        foreach (var entry in workerLog.Entries)
        {
            AssertNoPersonalData(entry.Text);
        }

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task REQ_028_clear_token_is_never_persisted_queue_holds_only_in_memory_items_and_has_no_database_dependency()
    {
        var queue = new ChannelPasswordResetNotifier(10, new SpyLogger<ChannelPasswordResetNotifier>());
        await queue.NotifyAsync(Player, Ana, Link);

        Assert.True(queue.Reader.TryRead(out var item));
        Assert.Equal(Link, item.ResetLink);

        var itemProperties = typeof(PasswordResetEmail)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["PlayerId", "ResetLink", "To"], itemProperties);

        var dependencies = typeof(ChannelPasswordResetNotifier).GetConstructors()
            .Concat(typeof(PasswordResetEmailWorker).GetConstructors())
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);
        Assert.DoesNotContain(dependencies, t => typeof(DbContext).IsAssignableFrom(t));
    }

    private static void AssertNoPersonalData(string text)
    {
        Assert.DoesNotContain(Recipient, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(OtherRecipient, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
        Assert.DoesNotContain("redefinir-senha", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Player.Value.ToString(), text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SenderCall(PlayerId PlayerId, Email To, string ResetLink);

    private sealed class FakeSender : IPasswordResetSender
    {
        private readonly object _lock = new();
        private readonly List<SenderCall> _calls = [];

        public TaskCompletionSource? Gate { get; init; }

        public bool FailFirst { get; init; }

        public IReadOnlyList<SenderCall> Calls
        {
            get
            {
                lock (_lock)
                {
                    return [.. _calls];
                }
            }
        }

        public async Task SendAsync(PlayerId playerId, Email recipient, string resetLink, CancellationToken ct = default)
        {
            int count;
            lock (_lock)
            {
                _calls.Add(new SenderCall(playerId, recipient, resetLink));
                count = _calls.Count;
            }

            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(ct);
            }

            if (FailFirst && count == 1)
            {
                throw new InvalidOperationException($"smtp down for {recipient.Value} {resetLink}");
            }
        }

        public async Task WaitForCallsAsync(int expected)
        {
            using var cts = new CancellationTokenSource(Wait);
            while (Calls.Count < expected)
            {
                await Task.Delay(10, cts.Token);
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Text);

    private sealed class SpyLogger<T> : ILogger<T>
    {
        private readonly object _lock = new();
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_lock)
                {
                    return [.. _entries];
                }
            }
        }

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
            lock (_lock)
            {
                _entries.Add(new LogEntry(logLevel, text));
            }
        }
    }
}
