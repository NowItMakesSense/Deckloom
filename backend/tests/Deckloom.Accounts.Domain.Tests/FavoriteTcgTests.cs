using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// T-010 (REQ-015, REQ-017, REQ-044): value object FavoriteTcg (enumeracao extensivel).
// Design (tabela de invariantes): "Conjunto suportado `{ mtg }` (rotulo "Magic: The Gathering"); padrao `mtg`
// na criacao; adicionar TCG e acrescentar um valor sem tocar perfis existentes".
// Contrato de erro (PUT /me/profile): favoriteTcg admite apenas `invalid`.
// Contrato assumido para o GREEN (ver relatorio):
//   FavoriteTcg.Create(string? id) : Result<FavoriteTcg>            -- usa o conjunto padrao { mtg }
//   Id exato, sensivel a maiusculas, sem trim; unica fonte: Supported
//   FavoriteTcg.Supported : IReadOnlyList<FavoriteTcg>; FavoriteTcg.Default; .Id; .Label; ToString() == Id
public class FavoriteTcgTests
{
    [Fact]
    public void REQ_017_supported_set_is_only_mtg_with_its_label()
    {
        var supported = FavoriteTcg.Supported;

        var only = Assert.Single(supported);
        Assert.Equal("mtg", only.Id);
        Assert.Equal("Magic: The Gathering", only.Label);
    }

    [Fact]
    public void REQ_044_default_is_mtg()
    {
        Assert.Equal("mtg", FavoriteTcg.Default.Id);
        Assert.Equal("Magic: The Gathering", FavoriteTcg.Default.Label);
        Assert.Equal(FavoriteTcg.Supported[0], FavoriteTcg.Default);
    }

    [Fact]
    public void REQ_017_mtg_is_accepted_and_equals_default()
    {
        var result = FavoriteTcg.Create("mtg");

        Assert.True(result.IsSuccess);
        Assert.Equal(FavoriteTcg.Default, result.Value);
    }

    [Fact]
    public void REQ_015_serialization_of_mtg_is_its_id()
    {
        Assert.Equal("mtg", FavoriteTcg.Default.ToString());
    }

    [Theory]
    [InlineData("Pokemon")]
    [InlineData("pokemon")]
    [InlineData("MTG")]
    [InlineData(" mtg ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void REQ_015_value_outside_the_supported_set_is_rejected_with_invalid(string? input)
    {
        var result = FavoriteTcg.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
    }

    [Fact]
    public void REQ_017_create_accepts_exactly_the_ids_in_supported()
    {
        Assert.NotEmpty(FavoriteTcg.Supported);

        foreach (var tcg in FavoriteTcg.Supported)
        {
            var result = FavoriteTcg.Create(tcg.Id);

            Assert.True(result.IsSuccess);
            Assert.Equal(tcg, result.Value);
            Assert.Equal(tcg.Id, result.Value.ToString());
        }
    }

    // R-04 / T-010 RED: extensibilidade. A lista suportada e injetavel por um seam interno
    // (InternalsVisibleTo); a API publica nao muda.

    private static readonly FavoriteTcg SecondTcg = new("other", "Outro TCG");

    private static IReadOnlyList<FavoriteTcg> ExtendedSet() => [FavoriteTcg.Default, SecondTcg];

    [Fact]
    public void REQ_017_registering_a_second_value_makes_it_creatable()
    {
        var result = FavoriteTcg.Create("other", ExtendedSet());

        Assert.True(result.IsSuccess);
        Assert.Equal("other", result.Value.Id);
        Assert.Equal("Outro TCG", result.Value.Label);
    }

    [Fact]
    public void REQ_017_registering_a_second_value_does_not_change_mtg_serialization_or_equality()
    {
        var result = FavoriteTcg.Create("mtg", ExtendedSet());

        Assert.True(result.IsSuccess);
        Assert.Equal("mtg", result.Value.ToString());
        Assert.Equal(FavoriteTcg.Default, result.Value);
        Assert.Equal(FavoriteTcg.Default.GetHashCode(), result.Value.GetHashCode());
        Assert.NotEqual(SecondTcg, result.Value);
    }

    [Fact]
    public void REQ_017_the_default_supported_set_stays_mtg_only_after_a_custom_set_is_used()
    {
        _ = FavoriteTcg.Create("other", ExtendedSet());

        Assert.Single(FavoriteTcg.Supported);
        Assert.True(FavoriteTcg.Create("other").IsFailure);
        Assert.Equal("invalid", FavoriteTcg.Create("other").Error.Code);
    }

    [Fact]
    public void REQ_015_a_value_outside_a_custom_set_is_rejected_with_invalid()
    {
        var result = FavoriteTcg.Create("pokemon", ExtendedSet());

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
    }
}
