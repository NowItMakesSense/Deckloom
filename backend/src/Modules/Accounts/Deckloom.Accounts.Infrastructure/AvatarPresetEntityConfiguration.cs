using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deckloom.Accounts.Infrastructure;

// Mapeamento de accounts.avatar_preset. No maximo um padrao: indice unico parcial em is_default.
internal sealed class AvatarPresetEntityConfiguration : IEntityTypeConfiguration<AvatarPresetRow>
{
    public const string TableName = "avatar_preset";
    public const string SingleDefaultIndexName = "ux_avatar_preset_single_default";

    public void Configure(EntityTypeBuilder<AvatarPresetRow> preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        preset.ToTable(TableName);
        preset.HasKey(p => p.Id).HasName("pk_avatar_preset");
        preset.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        preset.Property(p => p.Label).HasColumnName("label").IsRequired();
        preset.Property(p => p.ImagePath).HasColumnName("image_path").IsRequired();
        preset.Property(p => p.IsDefault).HasColumnName("is_default");
        preset.Property(p => p.IsActive).HasColumnName("is_active");
        preset.Property(p => p.SortOrder).HasColumnName("sort_order");
        preset.HasIndex(p => p.IsDefault)
            .IsUnique()
            .HasDatabaseName(SingleDefaultIndexName)
            .HasFilter("is_default = true");
        preset.HasData(InitialPresets());
    }

    // Carga inicial (REQ-018, REQ-022): avatares abstratos e neutros; so o primeiro e o padrao.
    private static AvatarPresetRow[] InitialPresets() =>
    [
        Preset("circulo-azul", "Círculo azul", isDefault: true, sortOrder: 1),
        Preset("quadrado-verde", "Quadrado verde", isDefault: false, sortOrder: 2),
        Preset("losango-laranja", "Losango laranja", isDefault: false, sortOrder: 3),
        Preset("estrela-roxa", "Estrela roxa", isDefault: false, sortOrder: 4),
        Preset("triangulo-vermelho", "Triângulo vermelho", isDefault: false, sortOrder: 5),
        Preset("onda-ciano", "Onda ciano", isDefault: false, sortOrder: 6),
        Preset("folha-verde", "Folha verde", isDefault: false, sortOrder: 7),
        Preset("chama-amarela", "Chama amarela", isDefault: false, sortOrder: 8),
    ];

    private static AvatarPresetRow Preset(string id, string label, bool isDefault, int sortOrder) =>
        new()
        {
            Id = id,
            Label = label,
            ImagePath = $"/avatars/presets/{id}.svg",
            IsDefault = isDefault,
            IsActive = true,
            SortOrder = sortOrder,
        };
}
