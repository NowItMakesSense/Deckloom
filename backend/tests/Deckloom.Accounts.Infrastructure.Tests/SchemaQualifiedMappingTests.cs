using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-066 (ADR-0015, DB1): os papeis tem search_path = pg_catalog, entao nenhuma tabela pode depender dele.
// Somente metadados do modelo; sem conexao.
public sealed class SchemaQualifiedMappingTests
{
    private static readonly string[] AllowedSchemas = ["accounts", "deck_suggestion"];

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>().UseSqlite("DataSource=:memory:").Options;
        using var context = new AccountsDbContext(options);
        return context.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void T066_model_declares_the_accounts_default_schema()
    {
        Assert.Equal("accounts", BuildModel().GetDefaultSchema());
    }

    [Fact]
    public void T066_every_mapped_entity_has_an_explicit_known_schema()
    {
        var offenders = BuildModel().GetEntityTypes()
            .Where(e => e.GetTableName() is not null && !AllowedSchemas.Contains(e.GetSchema()))
            .Select(e => $"{e.DisplayName()} -> schema '{e.GetSchema() ?? "(nulo, depende de search_path)"}'")
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void T066_no_entity_is_mapped_without_a_table_name()
    {
        var offenders = BuildModel().GetEntityTypes()
            .Where(e => e.GetTableName() is null && e.GetViewName() is null)
            .Select(e => e.DisplayName())
            .ToList();

        Assert.True(offenders.Count == 0, $"Entidades sem tabela: {string.Join(", ", offenders)}");
    }
}
