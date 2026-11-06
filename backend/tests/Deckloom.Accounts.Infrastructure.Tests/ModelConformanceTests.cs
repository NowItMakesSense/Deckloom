using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-066 (RED). Conformidade do modelo EF de Accounts com Database/ (tabelas, PKs, FKs, indices, colunas).
// Somente metadados do modelo: o contexto e criado com o provider SQLite sobre uma connection string em memoria
// que NUNCA e aberta; nenhum banco e consultado. Divergencia = falha que cita o objeto de Database/.
public sealed class ModelConformanceTests
{
    private const string Schema = AccountsDbContext.SchemaName;

    // Colunas de sistema do PostgreSQL mapeadas pelo EF (concorrencia otimista); nao constam das tabelas de Database/.
    private static readonly string[] SystemColumns = ["xmin"];

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>().UseSqlite("DataSource=:memory:").Options;
        using var context = new AccountsDbContext(options);
        return context.GetService<IDesignTimeModel>().Model;
    }

    private static List<IEntityType> EntitiesOf(IModel model, string schema, string table) =>
        model.GetEntityTypes().Where(e => e.GetTableName() == table && e.GetSchema() == schema).ToList();

    private static List<string> Columns(IEnumerable<IReadOnlyProperty> properties, string schema, string table)
    {
        var store = StoreObjectIdentifier.Table(table, schema);
        return properties.Select(p => p.GetColumnName(store)!).ToList();
    }

    [Fact]
    public void T066_model_has_exactly_the_accounts_tables_listed_in_Database_tables()
    {
        var model = BuildModel();
        var expected = DatabaseDocs.Tables(Schema).Select(t => t.Name).Order().ToList();

        var actual = model.GetEntityTypes()
            .Where(e => e.GetSchema() == Schema && e.GetTableName() is not null && !e.IsTableExcludedFromMigrations())
            .Select(e => e.GetTableName()!)
            .Distinct().Order().ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void T066_every_primary_key_in_Database_exists_with_the_same_name_and_columns()
    {
        var model = BuildModel();
        var problems = new List<string>();

        foreach (var doc in DatabaseDocs.PrimaryKeys(Schema))
        {
            var entities = EntitiesOf(model, doc.Schema, doc.Table);
            var key = entities.Select(e => e.FindPrimaryKey()).FirstOrDefault(k => k?.GetName() == doc.Name);
            if (key is null)
            {
                problems.Add($"{doc.Name}: PK ausente em {doc.Schema}.{doc.Table}.");
                continue;
            }

            var actual = Columns(key.Properties, doc.Schema, doc.Table);
            if (!actual.SequenceEqual(doc.Columns))
            {
                problems.Add($"{doc.Name}: colunas {string.Join(",", actual)} != Database/ {string.Join(",", doc.Columns)}.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void T066_every_foreign_key_in_Database_exists_with_target_columns_and_referential_action()
    {
        var model = BuildModel();
        var problems = new List<string>();

        foreach (var doc in DatabaseDocs.ForeignKeys(Schema))
        {
            var fk = EntitiesOf(model, doc.SourceSchema, doc.SourceTable)
                .SelectMany(e => e.GetForeignKeys())
                .FirstOrDefault(f => f.GetConstraintName() == doc.Name);
            if (fk is null)
            {
                problems.Add($"{doc.Name}: FK ausente em {doc.SourceSchema}.{doc.SourceTable}.");
                continue;
            }

            var expectedAction = doc.OnDelete switch
            {
                "cascade" => DeleteBehavior.Cascade,
                "restrict" => DeleteBehavior.Restrict,
                _ => throw new InvalidOperationException($"{doc.Name}: acao '{doc.OnDelete}' fora de CASCADE/RESTRICT."),
            };
            if (fk.DeleteBehavior != expectedAction)
            {
                problems.Add($"{doc.Name}: ON DELETE {fk.DeleteBehavior} != Database/ {expectedAction}.");
            }

            var principal = fk.PrincipalEntityType;
            if (principal.GetSchema() != doc.TargetSchema || principal.GetTableName() != doc.TargetTable)
            {
                problems.Add($"{doc.Name}: alvo {principal.GetSchema()}.{principal.GetTableName()} != {doc.TargetSchema}.{doc.TargetTable}.");
            }

            var source = Columns(fk.Properties, doc.SourceSchema, doc.SourceTable);
            var target = Columns(fk.PrincipalKey.Properties, doc.TargetSchema, doc.TargetTable);
            if (!source.SequenceEqual([doc.SourceColumn]) || !target.SequenceEqual([doc.TargetColumn]))
            {
                problems.Add($"{doc.Name}: colunas {string.Join(",", source)}->{string.Join(",", target)} != Database/ {doc.SourceColumn}->{doc.TargetColumn}.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void T066_every_index_in_Database_exists_with_columns_uniqueness_and_filter()
    {
        var model = BuildModel();
        var problems = new List<string>();

        foreach (var doc in DatabaseDocs.Indexes(Schema))
        {
            var index = EntitiesOf(model, doc.Schema, doc.Table)
                .SelectMany(e => e.GetIndexes())
                .FirstOrDefault(i => i.GetDatabaseName() == doc.Name);
            if (index is null)
            {
                problems.Add($"{doc.Name}: indice ausente em {doc.Schema}.{doc.Table}.");
                continue;
            }

            var actual = Columns(index.Properties, doc.Schema, doc.Table);
            if (!actual.SequenceEqual(doc.Columns))
            {
                problems.Add($"{doc.Name}: colunas {string.Join(",", actual)} != Database/ {string.Join(",", doc.Columns)}.");
            }

            if (index.IsUnique != doc.Unique)
            {
                problems.Add($"{doc.Name}: unique={index.IsUnique} != Database/ {doc.Unique}.");
            }

            if (index.GetFilter() != doc.Filter)
            {
                problems.Add($"{doc.Name}: filtro '{index.GetFilter()}' != Database/ '{doc.Filter}'.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void T066_model_has_no_index_beyond_Database_indexes()
    {
        var model = BuildModel();
        var documented = DatabaseDocs.Indexes(Schema).Select(i => i.Name).ToHashSet(StringComparer.Ordinal);

        var extra = model.GetEntityTypes()
            .Where(e => e.GetSchema() == Schema && !e.IsTableExcludedFromMigrations())
            .SelectMany(e => e.GetIndexes())
            .Select(i => i.GetDatabaseName()!)
            .Where(n => !documented.Contains(n))
            .Order().ToList();

        Assert.True(extra.Count == 0, $"Indices no modelo sem arquivo em Database/indexes: {string.Join(", ", extra)}.");
    }

    [Fact]
    public void T066_every_table_has_exactly_the_columns_listed_in_Database_tables()
    {
        var model = BuildModel();
        var problems = new List<string>();

        foreach (var doc in DatabaseDocs.Tables(Schema))
        {
            var entities = EntitiesOf(model, doc.Schema, doc.Name);
            if (entities.Count == 0)
            {
                problems.Add($"{doc.Schema}.{doc.Name}: tabela ausente no modelo.");
                continue;
            }

            var store = StoreObjectIdentifier.Table(doc.Name, doc.Schema);
            var properties = entities.SelectMany(e => e.GetProperties()).Where(p => p.GetColumnName(store) is not null).ToList();
            var actual = Columns(properties, doc.Schema, doc.Name).Except(SystemColumns).Order().ToList();
            var expected = doc.Columns.Select(c => c.Name).Order().ToList();
            if (!actual.SequenceEqual(expected))
            {
                var missing = expected.Except(actual);
                var extra = actual.Except(expected);
                problems.Add($"{doc.Name}: faltam [{string.Join(",", missing)}], sobram [{string.Join(",", extra)}].");
            }

            foreach (var column in doc.Columns.Where(c => c.Required is not null))
            {
                var property = properties.FirstOrDefault(p => p.GetColumnName(store) == column.Name);
                if (property is not null && property.IsColumnNullable(store) == column.Required)
                {
                    problems.Add($"{doc.Name}.{column.Name}: anulavel={property.IsColumnNullable(store)} != Database/ (not null={column.Required}).");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
