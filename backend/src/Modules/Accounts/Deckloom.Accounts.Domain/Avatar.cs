using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public abstract class Avatar : IEquatable<Avatar>
{
    public const string InvalidCode = "invalid";

    private protected Avatar()
    {
    }

    public static Result<Avatar> CreateDefault(string? defaultPresetId)
    {
        var preset = PresetAvatar.Create(defaultPresetId);

        return preset.IsSuccess
            ? Result<Avatar>.Success(preset.Value)
            : Result<Avatar>.Failure(preset.Error);
    }

    public bool Equals(Avatar? other) =>
        other is not null && GetType() == other.GetType() && ValueEquals(other);

    public override bool Equals(object? obj) => Equals(obj as Avatar);

    public override int GetHashCode() => HashCode.Combine(GetType(), ValueHashCode());

    private protected static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private protected abstract bool ValueEquals(Avatar other);

    private protected abstract int ValueHashCode();
}

public sealed class PresetAvatar : Avatar
{
    private PresetAvatar(string presetId) => PresetId = presetId;

    public string PresetId { get; }

    public static Result<PresetAvatar> Create(string? presetId) =>
        IsBlank(presetId)
            ? Result<PresetAvatar>.Failure(new DomainError(InvalidCode))
            : Result<PresetAvatar>.Success(new PresetAvatar(presetId!));

    private protected override bool ValueEquals(Avatar other) =>
        string.Equals(PresetId, ((PresetAvatar)other).PresetId, StringComparison.Ordinal);

    private protected override int ValueHashCode() => StringComparer.Ordinal.GetHashCode(PresetId);
}

public sealed class CustomAvatar : Avatar
{
    private CustomAvatar(string storageKey) => StorageKey = storageKey;

    public string StorageKey { get; }

    public static Result<CustomAvatar> Create(string? storageKey) =>
        IsBlank(storageKey)
            ? Result<CustomAvatar>.Failure(new DomainError(InvalidCode))
            : Result<CustomAvatar>.Success(new CustomAvatar(storageKey!));

    private protected override bool ValueEquals(Avatar other) =>
        string.Equals(StorageKey, ((CustomAvatar)other).StorageKey, StringComparison.Ordinal);

    private protected override int ValueHashCode() => StringComparer.Ordinal.GetHashCode(StorageKey);
}
