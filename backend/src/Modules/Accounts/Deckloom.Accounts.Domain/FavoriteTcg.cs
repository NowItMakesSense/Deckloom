using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record FavoriteTcg
{
    public const string InvalidCode = "invalid";

    private static readonly FavoriteTcg Mtg = new("mtg", "Magic: The Gathering");

    internal FavoriteTcg(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public static IReadOnlyList<FavoriteTcg> Supported { get; } = [Mtg];

    public static FavoriteTcg Default => Mtg;

    public string Id { get; }

    public string Label { get; }

    public static Result<FavoriteTcg> Create(string? id) => Create(id, Supported);

    internal static Result<FavoriteTcg> Create(string? id, IReadOnlyList<FavoriteTcg> supported)
    {
        var match = supported.FirstOrDefault(tcg => string.Equals(tcg.Id, id, StringComparison.Ordinal));

        return match is null
            ? Result<FavoriteTcg>.Failure(new DomainError(InvalidCode))
            : Result<FavoriteTcg>.Success(match);
    }

    public override string ToString() => Id;
}
