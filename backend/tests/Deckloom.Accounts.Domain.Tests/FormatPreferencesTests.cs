using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// T-011 (REQ-015, REQ-016, REQ-037, REQ-046): FormatIds do perfil.
// Design (tabela de invariantes): "`FormatIds`: Conjunto sem repeticao (duplicatas enviadas sao colapsadas), pode ser
// vazio; cada item novo deve ser Formato **ativo** do catalogo; referencias a Formato inativo ja guardadas sao preservadas".
// Design (PUT /me/profile): "o conjunto salvo = `formatIds` enviados (todos ativos, validados) U referencias ja existentes
// a Formatos inativos (REQ-046); a resposta so lista ativos. Se um Formato for reativado, ele volta a aparecer".
// Erro (PUT /me/profile): formatIds admite apenas `invalid`.
// Contrato assumido para o GREEN (ver relatorio):
//   FormatPreferences (sealed, ctor privado, imutavel); FormatPreferences.Empty
//   Update(IEnumerable<FormatId> requested, IReadOnlyCollection<FormatId> activeIds) : Result<FormatPreferences>
//   Visible(IReadOnlyCollection<FormatId> activeIds) : IReadOnlyCollection<FormatId>   -- so os ativos
//   Ids : IReadOnlyCollection<FormatId>   -- conjunto armazenado completo (ativos + inativos preservados)
//   FormatId = readonly record struct FormatId(string Value) (R-09; ids exatos, comparacao ordinal); a API expoe formatIds: string[]
//   Ids, Update e Visible usam FormatId; igualdade de FormatPreferences e por conjunto, sem ordem (R-05)
public class FormatPreferencesTests
{
    private static readonly FormatId[] AllActive = Ids("standard", "modern", "pioneer", "commander", "pauper");
    private static readonly FormatId[] ActiveWithoutPauper = Ids("standard", "modern", "pioneer", "commander");

    private static FormatId[] Ids(params string[] values) => [.. values.Select(value => new FormatId(value))];

    private static IEnumerable<string> Values(IEnumerable<FormatId> ids) =>
        ids.Select(id => id.Value).Order(StringComparer.Ordinal);

    private static FormatPreferences Saved(params string[] ids)
    {
        var result = FormatPreferences.Empty.Update(Ids(ids), AllActive);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public void REQ_016_empty_request_is_accepted()
    {
        var result = FormatPreferences.Empty.Update([], AllActive);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Ids);
        Assert.Empty(result.Value.Visible(AllActive));
    }

    [Fact]
    public void REQ_016_multiple_active_formats_are_stored()
    {
        var result = FormatPreferences.Empty.Update(Ids("modern", "commander"), AllActive);

        Assert.True(result.IsSuccess);
        Assert.Equal(["commander", "modern"], Values(result.Value.Ids));
    }

    [Fact]
    public void REQ_015_duplicates_are_collapsed()
    {
        var result = FormatPreferences.Empty.Update(Ids("modern", "modern", "pauper", "modern"), AllActive);

        Assert.True(result.IsSuccess);
        Assert.Equal(["modern", "pauper"], Values(result.Value.Ids));
    }

    [Fact]
    public void REQ_046_unknown_format_is_rejected_with_invalid()
    {
        var result = FormatPreferences.Empty.Update(Ids("FormatoInventado"), AllActive);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
    }

    [Fact]
    public void REQ_046_new_inactive_format_is_rejected_with_invalid()
    {

        var result = FormatPreferences.Empty.Update(Ids("modern", "pauper"), ActiveWithoutPauper);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
    }

    [Fact]
    public void REQ_046_one_invalid_item_rejects_the_whole_request_and_keeps_the_state()
    {
        var saved = Saved("modern");

        var result = saved.Update(Ids("commander", "FormatoInventado"), AllActive);

        Assert.True(result.IsFailure);
        Assert.Equal(["modern"], Values(saved.Ids));
    }

    [Fact]
    public void REQ_046_saving_without_an_already_stored_inactive_format_preserves_it()
    {
        var saved = Saved("modern", "pauper");

        var result = saved.Update(Ids("modern", "commander"), ActiveWithoutPauper);

        Assert.True(result.IsSuccess);
        Assert.Equal(["commander", "modern", "pauper"], Values(result.Value.Ids));
    }

    [Fact]
    public void REQ_046_saving_with_unchanged_request_keeps_the_inactive_format_stored()
    {
        var saved = Saved("modern", "pauper");

        var result = saved.Update(Ids("modern"), ActiveWithoutPauper);

        Assert.True(result.IsSuccess);
        Assert.Contains(new FormatId("pauper"), result.Value.Ids);
    }

    [Fact]
    public void REQ_046_visible_omits_inactive_formats()
    {
        var saved = Saved("modern", "pauper");

        var visible = saved.Visible(ActiveWithoutPauper);

        Assert.Equal(["modern"], Values(visible));
    }

    [Fact]
    public void REQ_046_reactivated_format_shows_up_again_without_player_action()
    {
        var saved = Saved("modern", "pauper");
        var afterSaveWhileInactive = saved.Update(Ids("modern"), ActiveWithoutPauper).Value;

        var visible = afterSaveWhileInactive.Visible(AllActive);

        Assert.Equal(["modern", "pauper"], Values(visible));
    }

    [Fact]
    public void REQ_046_resending_an_already_stored_inactive_format_is_accepted_and_kept()
    {
        var saved = Saved("modern", "pauper");

        var result = saved.Update(Ids("modern", "pauper"), ActiveWithoutPauper);

        Assert.True(result.IsSuccess);
        Assert.Equal(["modern", "pauper"], Values(result.Value.Ids));
        Assert.Equal(["modern"], Values(result.Value.Visible(ActiveWithoutPauper)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void REQ_046_null_empty_or_blank_id_is_rejected_with_invalid(string? id)
    {
        var result = FormatPreferences.Empty.Update(Ids(id!), AllActive);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
    }

    [Theory]
    [InlineData("Modern")]
    [InlineData("MODERN")]
    [InlineData(" modern ")]
    public void REQ_046_ids_are_exact_and_case_sensitive(string id)
    {
        var result = FormatPreferences.Empty.Update(Ids(id), AllActive);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid", result.Error.Code);
    }

    [Fact]
    public void REQ_037_membership_follows_the_catalog_passed_in_not_a_hardcoded_list()
    {
        var result = FormatPreferences.Empty.Update(Ids("brawl"), Ids("brawl"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["brawl"], Values(result.Value.Ids));
    }

    // R-09 (FormatId tipado) e R-05 (igualdade por valor, como conjunto)

    [Fact]
    public void REQ_046_format_id_has_value_equality_that_is_exact_and_case_sensitive()
    {
        Assert.Equal(new FormatId("modern"), new FormatId("modern"));
        Assert.Equal(new FormatId("modern").GetHashCode(), new FormatId("modern").GetHashCode());
        Assert.NotEqual(new FormatId("modern"), new FormatId("Modern"));
        Assert.Equal("modern", new FormatId("modern").Value);
    }

    [Fact]
    public void REQ_015_preferences_with_the_same_ids_in_any_order_are_equal()
    {
        var first = Saved("modern", "commander");
        var second = Saved("commander", "modern");

        Assert.True(first.Equals(second));
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void REQ_015_preferences_built_from_duplicates_equal_the_collapsed_set()
    {
        Assert.Equal(Saved("modern", "modern", "pauper"), Saved("pauper", "modern"));
    }

    [Fact]
    public void REQ_015_empty_preferences_equal_each_other()
    {
        var emptied = Saved("modern").Update([], AllActive).Value;

        Assert.True(emptied == FormatPreferences.Empty);
        Assert.Equal(FormatPreferences.Empty.GetHashCode(), emptied.GetHashCode());
    }

    [Fact]
    public void REQ_015_preferences_with_different_ids_are_not_equal()
    {
        var modern = Saved("modern");

        Assert.False(modern.Equals(Saved("commander")));
        Assert.True(modern != Saved("modern", "commander"));
        Assert.False(modern.Equals(FormatPreferences.Empty));
        Assert.False(modern.Equals(null));
        Assert.True(modern != null);
    }
}
