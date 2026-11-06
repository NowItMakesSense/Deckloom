using System.Threading.Channels;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Deckloom.Accounts.Infrastructure;

// T-074 (REQ-028): fila em memoria (ADR-0010). O token em claro vive apenas no item da fila; nada e persistido.
public interface IPasswordResetSender
{
    Task SendAsync(PlayerId playerId, Email recipient, string resetLink, CancellationToken ct = default);
}

public sealed record PasswordResetEmail(PlayerId PlayerId, Email To, string ResetLink);

public sealed partial class ChannelPasswordResetNotifier : IPasswordResetNotifier
{
    private readonly Channel<PasswordResetEmail> _channel;
    private readonly ILogger<ChannelPasswordResetNotifier> _logger;

    public ChannelPasswordResetNotifier(int capacity, ILogger<ChannelPasswordResetNotifier> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<PasswordResetEmail>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });
    }

    public ChannelReader<PasswordResetEmail> Reader => _channel.Reader;

    public Task NotifyAsync(PlayerId playerId, Email email, string resetLink, CancellationToken ct = default)
    {
        if (!_channel.Writer.TryWrite(new PasswordResetEmail(playerId, email, resetLink)))
        {
            LogQueueFull();
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fila de envio de redefinicao de senha cheia; solicitacao descartada.")]
    private partial void LogQueueFull();
}

public sealed partial class PasswordResetEmailWorker : BackgroundService
{
    private readonly ChannelPasswordResetNotifier _queue;
    private readonly IPasswordResetSender _sender;
    private readonly ILogger<PasswordResetEmailWorker> _logger;

    public PasswordResetEmailWorker(
        ChannelPasswordResetNotifier queue,
        IPasswordResetSender sender,
        ILogger<PasswordResetEmailWorker> logger)
    {
        _queue = queue;
        _sender = sender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await SendSafelyAsync(item, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento normal do host.
        }
    }

    private async Task SendSafelyAsync(PasswordResetEmail item, CancellationToken stoppingToken)
    {
        try
        {
            await _sender.SendAsync(item.PlayerId, item.To, item.ResetLink, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            LogSendFailed();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha no envio de email de redefinicao de senha; item descartado.")]
    private partial void LogSendFailed();
}
