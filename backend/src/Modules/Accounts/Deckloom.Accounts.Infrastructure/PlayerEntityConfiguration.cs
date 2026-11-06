using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Deckloom.Accounts.Infrastructure;

// Mapeamento de Database/tables/accounts.player.md. O Avatar (tres colunas) e sincronizado por AccountsDbContext
// atraves das propriedades sombra declaradas aqui; identidade externa tem tabela propria (owned); formatos
// preferidos sao linhas de PlayerPreferredFormat; o token de redefinicao tem tabela propria (owned, 1:0..1).
internal sealed class PlayerEntityConfiguration(Action<EntityTypeBuilder<Player>> configureConcurrencyToken)
    : IEntityTypeConfiguration<Player>
{
    public const string TableName = "player";
    public const string EmailIndexName = "ux_player_email";
    public const string ExternalIdentityTableName = "player_external_identity";
    public const string ExternalIdentityIndexName = "ux_player_external_identity_provider_subject";
    public const string ResetTokenTableName = "password_reset_token";
    public const string ResetTokenHashIndexName = "ux_password_reset_token_token_hash";
    public const string OwnerIdColumn = "PlayerId";
    public const string LinkedAtColumn = "LinkedAt";
    public const string AvatarKindColumn = "AvatarKind";
    public const string AvatarPresetIdColumn = "AvatarPresetId";
    public const string CustomAvatarKeyColumn = "CustomAvatarKey";
    public const string CreatedAtColumn = "CreatedAt";
    public const string UpdatedAtColumn = "UpdatedAt";
    public const string PresetKind = "preset";
    public const string CustomKind = "custom";

    private const int BioCheckLength = 1200;

    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> Utc =
        new(value => value.ToUniversalTime(), value => value);

    public void Configure(EntityTypeBuilder<Player> player)
    {
        ArgumentNullException.ThrowIfNull(player);

        // length() existe em PostgreSQL e SQLite e conta caracteres, como char_length() da especificacao.
        player.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("ck_player_email_length", $"length(email) <= {Email.MaxLength}");
            table.HasCheckConstraint("ck_player_bio_length", $"bio IS NULL OR length(bio) <= {BioCheckLength}");
            table.HasCheckConstraint("ck_player_avatar_kind", $"avatar_kind IN ('{PresetKind}', '{CustomKind}')");
            table.HasCheckConstraint("ck_player_adult_declared", "adult_declared = true");
            table.HasCheckConstraint(
                "ck_player_avatar_consistency",
                $"(avatar_kind = '{PresetKind}' AND avatar_preset_id IS NOT NULL AND custom_avatar_key IS NULL) "
                + $"OR (avatar_kind = '{CustomKind}' AND custom_avatar_key IS NOT NULL AND avatar_preset_id IS NULL)");
        });

        player.HasKey(p => p.Id).HasName("pk_player");
        player.Property(p => p.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PlayerId(value))
            .ValueGeneratedNever();

        player.Property(p => p.Email)
            .HasColumnName("email")
            .HasConversion(email => email.Value, value => Email.Create(value).Value)
            .IsRequired();
        player.HasIndex(p => p.Email).IsUnique().HasDatabaseName(EmailIndexName);

        player.Property(p => p.PasswordHash)
            .HasColumnName("password_hash")
            .HasConversion(hash => hash!.ToStoredString(), value => PasswordHash.Parse(value).Value);

        player.Property(p => p.DisplayName)
            .HasColumnName("display_name")
            .HasConversion(name => name.Value, value => DisplayName.Create(value).Value)
            .IsRequired();

        player.Property(p => p.Bio)
            .HasColumnName("bio")
            .HasConversion(bio => bio!.Value, value => Bio.Create(value).Value);

        player.Property(p => p.FavoriteTcg)
            .HasColumnName("favorite_tcg")
            .HasConversion(tcg => tcg.Id, value => FavoriteTcg.Create(value).Value)
            .HasDefaultValueSql($"'{FavoriteTcg.Default.Id}'")
            .IsRequired();

        player.Property<string>(AvatarKindColumn).HasColumnName("avatar_kind").IsRequired();
        player.Property<string?>(AvatarPresetIdColumn).HasColumnName("avatar_preset_id");
        player.HasIndex(AvatarPresetIdColumn).HasDatabaseName("ix_player_avatar_preset_id");
        player.HasOne<AvatarPresetRow>()
            .WithMany()
            .HasForeignKey(AvatarPresetIdColumn)
            .HasConstraintName("fk_player_avatar_preset")
            .OnDelete(DeleteBehavior.Restrict);
        player.Property<string?>(CustomAvatarKeyColumn).HasColumnName("custom_avatar_key");
        player.Property<DateTimeOffset>(CreatedAtColumn).HasColumnName("created_at");
        player.Property<DateTimeOffset>(UpdatedAtColumn).HasColumnName("updated_at");

        player.OwnsOne(p => p.TermsAcceptance, terms =>
        {
            terms.Property(t => t.TermsVersion).HasColumnName("terms_version").IsRequired();
            terms.Property(t => t.AcceptedAt).HasColumnName("terms_accepted_at");
            terms.Property(t => t.AdultDeclared).HasColumnName("adult_declared");
        });
        player.Navigation(p => p.TermsAcceptance).IsRequired();

        ConfigureExternalIdentity(player);
        ConfigureResetToken(player);

        player.Ignore(p => p.ExternalIdentity);
        player.Ignore(p => p.Avatar);
        player.Ignore(p => p.Formats);
        player.Ignore(p => p.DomainEvents);

        configureConcurrencyToken(player);
    }

    // Database/tables/accounts.player_external_identity.md: tabela propria, uma linha por provedor.
    // linked_at e uma propriedade sombra carimbada por AccountsDbContext (o dominio nao a conhece).
    private static void ConfigureExternalIdentity(EntityTypeBuilder<Player> player)
    {
        player.OwnsMany(p => p.ExternalIdentities, identity =>
        {
            identity.ToTable(
                ExternalIdentityTableName,
                table => table.HasCheckConstraint(
                    "ck_player_external_identity_provider",
                    $"provider IN ('{ExternalIdentity.GoogleProvider}')"));

            identity.WithOwner().HasForeignKey(OwnerIdColumn).HasConstraintName("fk_player_external_identity_player");
            identity.Property<PlayerId>(OwnerIdColumn)
                .HasColumnName("player_id")
                .HasConversion(id => id.Value, value => new PlayerId(value));
            identity.HasKey(OwnerIdColumn, nameof(ExternalIdentity.Provider)).HasName("pk_player_external_identity");
            identity.HasIndex(OwnerIdColumn).HasDatabaseName("ix_player_external_identity_player_id");
            identity.Property(i => i.Provider).HasColumnName("provider").IsRequired();
            identity.Property(i => i.Subject).HasColumnName("subject").IsRequired();
            identity.HasIndex(i => new { i.Provider, i.Subject })
                .IsUnique()
                .HasDatabaseName(ExternalIdentityIndexName);
            identity.Property<DateTimeOffset>(LinkedAtColumn).HasColumnName("linked_at");
        });
    }

    // Database/tables/accounts.password_reset_token.md: PK = player_id (no maximo um token por Jogador);
    // so o hash SHA-256 e persistido. Emitir substitui a linha; consumir apaga (1 linha afetada ou conflito).
    private static void ConfigureResetToken(EntityTypeBuilder<Player> player)
    {
        player.OwnsOne(p => p.PasswordResetToken, token =>
        {
            token.ToTable(ResetTokenTableName);

            token.WithOwner().HasForeignKey(OwnerIdColumn).HasConstraintName("fk_password_reset_token_player");
            token.Property<PlayerId>(OwnerIdColumn)
                .HasColumnName("player_id")
                .HasConversion(id => id.Value, value => new PlayerId(value));
            token.HasKey(OwnerIdColumn).HasName("pk_password_reset_token");
            token.Property(t => t.TokenHash)
                .HasColumnName("token_hash")
                .HasConversion(hash => hash.ToBytes(), bytes => TokenHash.Create(bytes).Value)
                .IsRequired();
            token.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName(ResetTokenHashIndexName);
            token.Property(t => t.IssuedAt).HasColumnName("issued_at").HasConversion(Utc);
            token.Property(t => t.ExpiresAt).HasColumnName("expires_at").HasConversion(UtcComparableConverter.Instance);
        });
    }
}
