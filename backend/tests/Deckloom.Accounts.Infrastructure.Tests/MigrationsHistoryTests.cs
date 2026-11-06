using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-066 (RED, ADR-0015 DB1): a tabela de historico vive em "accounts", pois o search_path dos papeis e pg_catalog.
// Layout decidido pelo orquestrador: a migracao do modulo fica no proprio projeto Infrastructure, com uma
// IDesignTimeDbContextFactory que NAO abre conexao (connection string ficticia, escrita no codigo).
public sealed class MigrationsHistoryTests
{
    private const string FactoryInterface = "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory`1";

    private static DbContext CreateFromDesignTimeFactory()
    {
        var assembly = typeof(AccountsDbContext).Assembly;
        var factory = assembly.GetTypes().FirstOrDefault(t => !t.IsAbstract && t.GetInterfaces().Any(i =>
            i.IsGenericType
            && i.GetGenericTypeDefinition().FullName == FactoryInterface
            && i.GetGenericArguments()[0] == typeof(AccountsDbContext)));

        Assert.True(
            factory is not null,
            "Fabrica de tempo de design ausente: falta uma IDesignTimeDbContextFactory<AccountsDbContext> em "
            + "Deckloom.Accounts.Infrastructure (T-066 GREEN).");

        var instance = Activator.CreateInstance(factory, nonPublic: true)!;
        var create = factory.GetMethod("CreateDbContext", BindingFlags.Public | BindingFlags.Instance, [typeof(string[])])!;
        return (DbContext)create.Invoke(instance, [Array.Empty<string>()])!;
    }

    [Fact]
    public void T066_accounts_module_configures_the_history_table_in_its_own_schema()
    {
        using var context = CreateFromDesignTimeFactory();

        var relational = context.GetService<IDbContextOptions>().Extensions
            .OfType<RelationalOptionsExtension>().SingleOrDefault();

        Assert.True(relational is not null, "Contexto sem provider relacional configurado pela fabrica.");
        Assert.Equal("__ef_migrations_history", relational.MigrationsHistoryTableName);
        Assert.Equal("accounts", relational.MigrationsHistoryTableSchema);
    }

    [Fact]
    public void T066_design_time_factory_does_not_open_a_connection()
    {
        using var context = CreateFromDesignTimeFactory();

        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
