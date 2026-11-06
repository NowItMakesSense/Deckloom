using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed class FormatPreferences : IEquatable<FormatPreferences>
{
    public const string InvalidCode = "invalid";

    private FormatPreferences(IReadOnlyCollection<FormatId> ids)
    {
        Ids = ids;
    }

    public static FormatPreferences Empty { get; } = new([]);

    public IReadOnlyCollection<FormatId> Ids { get; }

    public Result<FormatPreferences> Update(IEnumerable<FormatId> requested, IReadOnlyCollection<FormatId> activeIds)
    {
        var distinct = requested.Distinct().ToList();

        if (distinct.Any(id => !IsAcceptable(id, activeIds)))
        {
            return Result<FormatPreferences>.Failure(new DomainError(InvalidCode));
        }

        var preservedInactive = Ids.Where(id => !activeIds.Contains(id));

        return Result<FormatPreferences>.Success(new FormatPreferences([.. distinct.Union(preservedInactive)]));
    }

    public IReadOnlyCollection<FormatId> Visible(IReadOnlyCollection<FormatId> activeIds) =>
        [.. Ids.Where(activeIds.Contains)];

    public bool Equals(FormatPreferences? other) =>
        other is not null && ToSet(Ids).SetEquals(ToSet(other.Ids));

    public override bool Equals(object? obj) => Equals(obj as FormatPreferences);

    public static bool operator ==(FormatPreferences? left, FormatPreferences? right) => Equals(left, right);

    public static bool operator !=(FormatPreferences? left, FormatPreferences? right) => !Equals(left, right);

    public override int GetHashCode() =>
        ToSet(Ids).Aggregate(0, (hash, id) => hash ^ StringComparer.Ordinal.GetHashCode(id.Value ?? string.Empty));

    private static HashSet<FormatId> ToSet(IEnumerable<FormatId> ids) => [.. ids];

    private bool IsAcceptable(FormatId id, IReadOnlyCollection<FormatId> activeIds) =>
        !string.IsNullOrWhiteSpace(id.Value) && (activeIds.Contains(id) || Ids.Contains(id));
}
