using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed record UpdateMyProfileCommand(
    AuthenticatedPlayer? Caller,
    string? DisplayName,
    string? Bio,
    string? FavoriteTcg,
    IReadOnlyList<string>? FormatIds);

public sealed class UpdateMyProfile(
    IPlayerRepository players,
    IFormatCatalog formats,
    IAvatarPresetCatalog avatarPresetCatalog,
    IUnitOfWork unitOfWork,
    IAccountsEventLog? events = null)
{
    private static readonly DomainError Unauthenticated = new("unauthenticated");
    private static readonly DomainError ConcurrencyConflict = new("concurrency_conflict");

    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    public async Task<Result<Profile>> ExecuteAsync(UpdateMyProfileCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Caller is null)
        {
            return Result<Profile>.Failure(Unauthenticated);
        }

        var player = await players.GetById(command.Caller.PlayerId, ct);
        if (player is null)
        {
            return Result<Profile>.Failure(Unauthenticated);
        }

        var activeIds = (await formats.ListActiveAsync(ct)).Select(f => f.Id).ToList();
        var requested = (command.FormatIds ?? []).Select(id => new FormatId(id)).ToList();

        var validation = new ValidationErrorCollector();
        validation.Collect(DisplayName.Create(command.DisplayName), "displayName");
        validation.Collect(Bio.Create(command.Bio), "bio");
        validation.Collect(FavoriteTcg.Create(command.FavoriteTcg), "favoriteTcg");
        validation.Collect(player.Formats.Update(requested, activeIds), "formatIds");
        if (validation.HasErrors)
        {
            return Result<Profile>.Failure(validation.Errors);
        }

        var eventsBefore = player.DomainEvents.Count;
        try
        {
            await unitOfWork.ExecuteAsync(async innerCt =>
            {
                player.UpdateProfile(command.DisplayName, command.Bio, command.FavoriteTcg, requested, activeIds);
                await players.Save(player, innerCt);
            }, ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result<Profile>.Failure(ConcurrencyConflict);
        }

        var changed = player.DomainEvents.Skip(eventsBefore).OfType<ProfileUpdated>().FirstOrDefault();
        if (changed is not null)
        {
            _events.ProfileUpdated(player.Id, changed.ChangedFields);
        }

        return Result<Profile>.Success(await new ProfileBuilder(formats, avatarPresetCatalog).BuildAsync(player, ct));
    }
}
