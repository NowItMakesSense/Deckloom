using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed record ChooseAvatarPresetCommand(AuthenticatedPlayer? Caller, string? PresetId);

public sealed class ChooseAvatarPreset(
    IPlayerRepository players,
    IAvatarPresetCatalog avatarPresetCatalog,
    IAvatarStorage avatarStorage,
    IUnitOfWork unitOfWork,
    IAccountsEventLog? events = null)
{
    private static readonly DomainError Unauthenticated = new("unauthenticated");
    private static readonly DomainError InvalidPreset = new("invalid", "presetId");

    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    public async Task<Result<AvatarView>> ExecuteAsync(ChooseAvatarPresetCommand command, CancellationToken ct = default)
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

        if (string.IsNullOrWhiteSpace(command.PresetId)
            || !await avatarPresetCatalog.ContainsActiveAsync(command.PresetId, ct))
        {
            return Result<AvatarView>.Failure(InvalidPreset);
        }

        var eventsBefore = player.DomainEvents.Count;
        var change = player.ChooseAvatarPreset(command.PresetId);
        if (change.IsFailure)
        {
            return Result<AvatarView>.Failure(InvalidPreset);
        }

        var previousKey = player.DomainEvents.Skip(eventsBefore).OfType<AvatarChanged>().FirstOrDefault()?.PreviousStorageKey;
        var remover = new PreviousAvatarRemover(avatarStorage);

        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            await players.Save(player, innerCt);
            await remover.RemoveAsync(previousKey, innerCt);
        }, ct);

        _events.AvatarChanged(player.Id, "preset");
        return Result<AvatarView>.Success(await new AvatarViewBuilder(avatarPresetCatalog).BuildAsync(player.Avatar, ct));
    }
}
