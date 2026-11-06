using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed record UploadCustomAvatarCommand(AuthenticatedPlayer? Caller, byte[]? Image);

public sealed class UploadCustomAvatar(
    IPlayerRepository players,
    IAvatarPresetCatalog avatarPresetCatalog,
    IAvatarImageProcessor imageProcessor,
    IAvatarStorage avatarStorage,
    IUnitOfWork unitOfWork,
    IAccountsEventLog? events = null)
{
    private static readonly DomainError Unauthenticated = new("unauthenticated");

    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    public async Task<Result<AvatarView>> ExecuteAsync(UploadCustomAvatarCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Caller is null)
        {
            return Result<AvatarView>.Failure(Unauthenticated);
        }

        var player = await players.GetById(command.Caller.PlayerId, ct);
        if (player is null)
        {
            return Result<AvatarView>.Failure(Unauthenticated);
        }

        var processed = await ProcessAsync(command.Image, ct);
        if (processed is not AvatarAccepted accepted)
        {
            var reason = (processed as AvatarRejected)?.Reason ?? AvatarRejectionReason.Content;
            return Result<AvatarView>.Failure(Reject(player, reason));
        }

        var storageKey = Guid.NewGuid().ToString("N");
        var eventsBefore = player.DomainEvents.Count;
        var change = player.SetCustomAvatar(storageKey);
        if (change.IsFailure)
        {
            return Result<AvatarView>.Failure(Reject(player, AvatarRejectionReason.Content));
        }

        var previousKey = player.DomainEvents.Skip(eventsBefore).OfType<AvatarChanged>().FirstOrDefault()?.PreviousStorageKey;
        var remover = new PreviousAvatarRemover(avatarStorage);

        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            await avatarStorage.SaveAsync(storageKey, accepted.Image, innerCt);
            await players.Save(player, innerCt);
            await remover.RemoveAsync(previousKey, innerCt);
        }, ct);

        _events.AvatarChanged(player.Id, "custom");
        return Result<AvatarView>.Success(await new AvatarViewBuilder(avatarPresetCatalog).BuildAsync(player.Avatar, ct));
    }

    // Conta a rejeicao (metrica) e registra o evento 1015; o detalhe nao e conhecido neste nivel.
    private DomainError Reject(Player player, AvatarRejectionReason reason)
    {
        var error = AvatarRejection.Reject(reason);
        _events.AvatarRejected(player.Id, error.Field!, null);
        return error;
    }

    // Falha do codec (excecao) vira rejeicao `content`; imagem ausente tambem.
    private async Task<AvatarProcessingResult?> ProcessAsync(byte[]? image, CancellationToken ct)
    {
        if (image is null)
        {
            return null;
        }

        try
        {
            return await imageProcessor.ProcessAsync(image, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
