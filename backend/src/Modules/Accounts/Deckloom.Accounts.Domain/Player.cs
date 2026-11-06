using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed class Player : AggregateRoot
{
    public const string NoLocalPasswordCode = "no_local_password";
    public const string AlreadyLinkedCode = "already_linked";
    public const string EmailMismatchCode = "email_mismatch";
    public const string InvalidProofCode = "invalid_proof";
    public const string ResetLinkInvalidCode = "reset_link_invalid";
    public const string RequiredCode = "required";
    public const string ReauthRequiredCode = "reauth_required";
    public const string ReauthExpiredCode = "reauth_expired";
    public const string ConfirmField = "confirm";

    private static readonly TimeSpan PasswordResetWindow = TimeSpan.FromMinutes(60);

    private readonly List<ExternalIdentity> _externalIdentities = [];

    // Usado so pela persistencia (EF Core) para materializar o agregado; os campos sao preenchidos pelo mapeamento.
    private Player()
    {
        Email = null!;
        DisplayName = null!;
        Avatar = null!;
        TermsAcceptance = null!;
    }

    private Player(
        PlayerId id,
        Email email,
        PasswordHash? passwordHash,
        ExternalIdentity? externalIdentity,
        DisplayName displayName,
        Avatar avatar,
        TermsAcceptance termsAcceptance)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        if (externalIdentity is not null)
        {
            _externalIdentities.Add(externalIdentity);
        }

        DisplayName = displayName;
        Avatar = avatar;
        TermsAcceptance = termsAcceptance;
    }

    public PlayerId Id { get; }

    public Email Email { get; }

    public PasswordHash? PasswordHash { get; private set; }

    // Um vinculo por provedor (accounts.player_external_identity); hoje so ha o Google.
    public IReadOnlyCollection<ExternalIdentity> ExternalIdentities => _externalIdentities;

    public ExternalIdentity? ExternalIdentity => _externalIdentities.FirstOrDefault();

    public PasswordResetToken? PasswordResetToken { get; private set; }

    public DisplayName DisplayName { get; private set; }

    public Bio? Bio { get; private set; }

    public FavoriteTcg FavoriteTcg { get; private set; } = FavoriteTcg.Default;

    public Avatar Avatar { get; private set; }

    public FormatPreferences Formats { get; private set; } = FormatPreferences.Empty;

    public TermsAcceptance TermsAcceptance { get; }

    public static Result<Player> RegisterWithPassword(
        Email email,
        PasswordHash passwordHash,
        DisplayName displayName,
        string currentTermsVersion,
        string? submittedTermsVersion,
        bool acceptTerms,
        bool adultDeclared,
        DateTimeOffset now,
        string? defaultPresetId)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(passwordHash);
        ArgumentNullException.ThrowIfNull(displayName);

        return Register(
            email,
            passwordHash,
            null,
            displayName,
            RegistrationMethod.Password,
            currentTermsVersion,
            submittedTermsVersion,
            acceptTerms,
            adultDeclared,
            now,
            defaultPresetId);
    }

    public static Result<Player> RegisterWithGoogle(
        Email email,
        ExternalIdentity externalIdentity,
        DisplayName displayName,
        string currentTermsVersion,
        string? submittedTermsVersion,
        bool acceptTerms,
        bool adultDeclared,
        DateTimeOffset now,
        string? defaultPresetId)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(externalIdentity);
        ArgumentNullException.ThrowIfNull(displayName);

        return Register(
            email,
            null,
            externalIdentity,
            displayName,
            RegistrationMethod.Google,
            currentTermsVersion,
            submittedTermsVersion,
            acceptTerms,
            adultDeclared,
            now,
            defaultPresetId);
    }

    public Result<Player> UpdateProfile(
        string? displayName,
        string? bio,
        string? favoriteTcg,
        IEnumerable<FormatId> formatIds,
        IReadOnlyCollection<FormatId> activeFormatIds)
    {
        ArgumentNullException.ThrowIfNull(formatIds);
        ArgumentNullException.ThrowIfNull(activeFormatIds);

        var errors = new List<DomainError>();
        var newName = Validated(DisplayName.Create(displayName), "displayName", errors);
        var newBio = Validated(Bio.Create(bio), "bio", errors);
        var newTcg = Validated(FavoriteTcg.Create(favoriteTcg), "favoriteTcg", errors);
        var newFormats = Validated(Formats.Update(formatIds, activeFormatIds), "formatIds", errors);

        if (errors.Count > 0)
        {
            return Result<Player>.Failure(errors);
        }

        var changed = new List<string>();
        if (newName != DisplayName)
        {
            changed.Add("displayName");
        }

        if (newBio != Bio)
        {
            changed.Add("bio");
        }

        if (newTcg != FavoriteTcg)
        {
            changed.Add("favoriteTcg");
        }

        if (newFormats != Formats)
        {
            changed.Add("formatIds");
        }

        DisplayName = newName!;
        Bio = newBio;
        FavoriteTcg = newTcg!;
        Formats = newFormats!;

        if (changed.Count > 0)
        {
            Raise(new ProfileUpdated(Id, changed));
        }

        return Result<Player>.Success(this);
    }

    public Result<Player> ChooseAvatarPreset(string? presetId) =>
        ChangeAvatar(PresetAvatar.Create(presetId));

    public Result<Player> SetCustomAvatar(string? storageKey) =>
        ChangeAvatar(CustomAvatar.Create(storageKey));

    public Result<Player> LinkGoogle(ExternalIdentity identity, Email verifiedEmail, PasswordProof? proof)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(verifiedEmail);

        var code = LinkRefusal(verifiedEmail, proof);
        if (code is not null)
        {
            return Result<Player>.Failure(new DomainError(code));
        }

        _externalIdentities.Clear();
        _externalIdentities.Add(identity);
        Raise(new GoogleIdentityLinked(Id));

        return Result<Player>.Success(this);
    }

    public Result<Player> IssuePasswordReset(TokenHash tokenHash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        if (PasswordHash is null)
        {
            return Result<Player>.Failure(new DomainError(NoLocalPasswordCode));
        }

        var expiresAt = now + PasswordResetWindow;
        PasswordResetToken = new PasswordResetToken(tokenHash, now, expiresAt);
        Raise(new PasswordResetIssued(Id, expiresAt));

        return Result<Player>.Success(this);
    }

    // Migracao transparente do hash apos login (REQ-038): nao e troca pelo usuario, sem evento nem revogacao.
    public Result<Player> RehashPassword(PasswordHash newHash)
    {
        ArgumentNullException.ThrowIfNull(newHash);

        if (PasswordHash is null)
        {
            return Result<Player>.Failure(new DomainError(NoLocalPasswordCode));
        }

        PasswordHash = newHash;
        return Result<Player>.Success(this);
    }

    public Result<Player> ResetPassword(
        TokenHash presentedTokenHash,
        PasswordHash newHash,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(presentedTokenHash);
        ArgumentNullException.ThrowIfNull(newHash);

        var token = PasswordResetToken;
        if (token is null || !token.TokenHash.Equals(presentedTokenHash) || now >= token.ExpiresAt)
        {
            return Result<Player>.Failure(new DomainError(ResetLinkInvalidCode));
        }

        PasswordHash = newHash;
        PasswordResetToken = null;
        Raise(new PasswordChanged(Id));

        return Result<Player>.Success(this);
    }

    // Retencao (REQ-047): limpa so o token vencido (mesma regra de ResetPassword: now >= ExpiresAt); o Jogador permanece.
    public bool ClearExpiredResetToken(DateTimeOffset now)
    {
        if (PasswordResetToken is null || now < PasswordResetToken.ExpiresAt)
        {
            return false;
        }

        PasswordResetToken = null;
        return true;
    }

    public Result<Player> Delete(bool confirmed, Reauthentication? reauthentication, DateTimeOffset now)
    {
        var error = DeletionRefusal(confirmed, reauthentication, now);
        if (error is not null)
        {
            return Result<Player>.Failure(error);
        }

        Raise(new PlayerDeleted(Id));

        return Result<Player>.Success(this);
    }

    private DomainError? DeletionRefusal(bool confirmed, Reauthentication? reauthentication, DateTimeOffset now)
    {
        if (!confirmed)
        {
            return new DomainError(RequiredCode, ConfirmField);
        }

        if (reauthentication is not null && reauthentication.PlayerId != Id)
        {
            return new DomainError(InvalidProofCode);
        }

        return PasswordHash is not null
            ? PasswordReauthRefusal(reauthentication)
            : GoogleReauthRefusal(reauthentication, now);
    }

    private static DomainError? PasswordReauthRefusal(Reauthentication? reauthentication) =>
        reauthentication is PasswordProof ? null : new DomainError(ReauthRequiredCode);

    private static DomainError? GoogleReauthRefusal(Reauthentication? reauthentication, DateTimeOffset now)
    {
        if ((reauthentication as RecentGoogleLogin)?.ReauthenticatedAt is not { } reauthenticatedAt)
        {
            return new DomainError(ReauthRequiredCode);
        }

        return reauthenticatedAt < now - Reauthentication.Window ? new DomainError(ReauthExpiredCode) : null;
    }

    private string? LinkRefusal(Email verifiedEmail, PasswordProof? proof)
    {
        if (PasswordHash is null)
        {
            return NoLocalPasswordCode;
        }

        if (ExternalIdentity is not null)
        {
            return AlreadyLinkedCode;
        }

        if (verifiedEmail != Email)
        {
            return EmailMismatchCode;
        }

        return proof is null || proof.PlayerId != Id ? InvalidProofCode : null;
    }

    private Result<Player> ChangeAvatar<T>(Result<T> candidate)
        where T : Avatar
    {
        var errors = new List<DomainError>();
        var newAvatar = Validated(candidate, "avatar", errors);
        if (errors.Count > 0)
        {
            return Result<Player>.Failure(errors);
        }

        if (!newAvatar!.Equals(Avatar))
        {
            var previousKey = (Avatar as CustomAvatar)?.StorageKey;
            Avatar = newAvatar;
            Raise(new AvatarChanged(Id, previousKey));
        }

        return Result<Player>.Success(this);
    }

    private static Result<Player> Register(
        Email email,
        PasswordHash? passwordHash,
        ExternalIdentity? externalIdentity,
        DisplayName displayName,
        RegistrationMethod method,
        string currentTermsVersion,
        string? submittedTermsVersion,
        bool acceptTerms,
        bool adultDeclared,
        DateTimeOffset now,
        string? defaultPresetId)
    {
        var terms = TermsAcceptance.Create(currentTermsVersion, submittedTermsVersion, adultDeclared, acceptTerms, now);
        if (terms.IsFailure)
        {
            return Result<Player>.Failure(terms.Error);
        }

        var avatar = Avatar.CreateDefault(defaultPresetId);
        if (avatar.IsFailure)
        {
            return Result<Player>.Failure(avatar.Error);
        }

        var player = new Player(
            new PlayerId(Guid.NewGuid()),
            email,
            passwordHash,
            externalIdentity,
            displayName,
            avatar.Value,
            terms.Value);
        player.Raise(new PlayerRegistered(player.Id, method, terms.Value.TermsVersion));

        return Result<Player>.Success(player);
    }

    private static T? Validated<T>(Result<T> result, string field, List<DomainError> errors)
    {
        if (result.IsSuccess)
        {
            return result.Value;
        }

        errors.AddRange(result.Errors.Select(error => error with { Field = field }));
        return default;
    }
}
