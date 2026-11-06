using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// T-012 (REQ-018, REQ-022): value object Avatar (soma fechada PresetAvatar | CustomAvatar).
// Design (tabela de invariantes): "`PresetAvatar(PresetId)` ou `CustomAvatar(StorageKey)`; criacao sempre com o
// preset padrao" (REQ-018, REQ-022); J6: "Exatamente um `Avatar`; criacao com preset padrao".
// Tasks T-012: "soma fechada (exatamente um); `PresetAvatar` exige `PresetId` nao vazio; `CustomAvatar` exige
// `StorageKey`; criacao padrao usa o preset padrao informado; igualdade por valor"; REFACTOR: hierarquia selada.
// Contrato de erro: PUT /me/avatar/preset admite `presetId` = `invalid`; o design nao define codigo para StorageKey
// vazia (assumido `invalid`, ver relatorio).
// Contrato assumido para o GREEN (ver relatorio):
//   abstract record Avatar  (construtor nao publico; unicos derivados: PresetAvatar e CustomAvatar, ambos sealed)
//   PresetAvatar.Create(string? presetId) : Result<PresetAvatar>   -- .PresetId
//   CustomAvatar.Create(string? storageKey) : Result<CustomAvatar> -- .StorageKey
//   Avatar.CreateDefault(string? defaultPresetId) : Result<Avatar> -- devolve PresetAvatar
//   Sem construtores publicos nos tipos; vazio/nulo/so espacos = `invalid`; valor mantido exato (sem trim).
public class AvatarTests
{
    private const string DefaultPresetId = "preset-01";

    [Fact]
    public void REQ_018_preset_avatar_holds_the_chosen_preset_id()
    {
        var result = PresetAvatar.Create("preset-03");

        Assert.True(result.IsSuccess);
        Assert.Equal("preset-03", result.Value.PresetId);
        Assert.IsAssignableFrom<Avatar>(result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void REQ_018_preset_avatar_requires_a_non_empty_preset_id(string? presetId)
    {
        var result = PresetAvatar.Create(presetId);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
    }

    [Fact]
    public void REQ_021_custom_avatar_holds_the_storage_key()
    {
        var result = CustomAvatar.Create("a1b2c3d4e5f60718293a4b5c6d7e8f90");

        Assert.True(result.IsSuccess);
        Assert.Equal("a1b2c3d4e5f60718293a4b5c6d7e8f90", result.Value.StorageKey);
        Assert.IsAssignableFrom<Avatar>(result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void REQ_019_custom_avatar_requires_a_storage_key(string? storageKey)
    {
        var result = CustomAvatar.Create(storageKey);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
    }

    [Fact]
    public void REQ_022_default_creation_uses_the_informed_default_preset()
    {
        var result = Avatar.CreateDefault(DefaultPresetId);

        Assert.True(result.IsSuccess);
        var preset = Assert.IsType<PresetAvatar>(result.Value);
        Assert.Equal(DefaultPresetId, preset.PresetId);
        Assert.Equal(PresetAvatar.Create(DefaultPresetId).Value, result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void REQ_022_default_creation_without_a_default_preset_is_rejected(string? presetId)
    {
        var result = Avatar.CreateDefault(presetId);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
    }

    [Fact]
    public void REQ_018_presets_with_the_same_id_are_equal_by_value()
    {
        var a = PresetAvatar.Create("preset-02").Value;
        var b = PresetAvatar.Create("preset-02").Value;
        var other = PresetAvatar.Create("preset-03").Value;

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, other);
    }

    [Fact]
    public void REQ_021_custom_avatars_with_the_same_key_are_equal_by_value()
    {
        var a = CustomAvatar.Create("key-1").Value;
        var b = CustomAvatar.Create("key-1").Value;
        var other = CustomAvatar.Create("key-2").Value;

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, other);
    }

    [Fact]
    public void REQ_018_a_preset_never_equals_a_custom_avatar_even_with_the_same_text()
    {
        Avatar preset = PresetAvatar.Create("same").Value;
        Avatar custom = CustomAvatar.Create("same").Value;

        Assert.NotEqual(preset, custom);
    }

    [Fact]
    public void REQ_018_values_are_kept_exactly_without_trimming()
    {
        Assert.Equal(" p ", PresetAvatar.Create(" p ").Value.PresetId);
        Assert.Equal(" k ", CustomAvatar.Create(" k ").Value.StorageKey);
    }

    [Fact]
    public void REQ_018_the_sum_is_closed_with_exactly_preset_and_custom_all_sealed()
    {
        var avatarType = typeof(Avatar);

        var derived = avatarType.Assembly.GetTypes()
            .Where(t => t != avatarType && avatarType.IsAssignableFrom(t))
            .ToList();

        Assert.Equal(2, derived.Count);
        Assert.Contains(typeof(PresetAvatar), derived);
        Assert.Contains(typeof(CustomAvatar), derived);
        Assert.All(derived, t => Assert.True(t.IsSealed, $"{t.Name} must be sealed"));
        Assert.True(avatarType.IsAbstract);
    }

    [Fact]
    public void REQ_018_nobody_outside_the_domain_can_extend_or_instantiate_the_types_directly()
    {
        foreach (var type in new[] { typeof(Avatar), typeof(PresetAvatar), typeof(CustomAvatar) })
        {
            var publicConstructors = type.GetConstructors(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            Assert.Empty(publicConstructors);
        }
    }

    [Fact]
    public void REQ_018_no_avatar_constructor_is_reachable_by_types_derived_in_other_assemblies()
    {
        var constructors = typeof(Avatar).GetConstructors(
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);

        Assert.NotEmpty(constructors);
        Assert.All(constructors, c =>
        {
            Assert.False(c.IsPublic, $"{c} must not be public");
            Assert.False(c.IsFamily, $"{c} must not be protected");
            Assert.False(c.IsFamilyOrAssembly, $"{c} must not be protected internal");
        });
    }
}
