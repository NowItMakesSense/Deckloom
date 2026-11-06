namespace Deckloom.ArchitectureTests;

/// <summary>
/// T-131 (regra 9 de T-002): nenhuma migração nem DDL em tempo de execução em backend/src (DB6; ADR-0015:
/// deckloom_app não tem CREATE). Varredura sintática; nada é executado.
/// backend/tests/ fica FORA da varredura de propósito: as fixtures SQLite dos testes de infraestrutura usam
/// EnsureCreated legitimamente para montar o esquema em memória.
/// </summary>
public class NoRuntimeMigrationTests
{
    private const string DesignRule = "Nenhuma DDL nem migração em tempo de execução (DB6, ADR-0015)";

    [Fact]
    public void T131_production_code_never_invokes_migration_or_ddl_members()
    {
        var violations = ProductionFiles()
            .SelectMany(f => RuntimeMigrationScanner.FindInvocations(File.ReadAllText(f))
                .Select(v => $"{Relative(f)}:{v.Line} chama {v.Member}()"))
            .ToList();

        Assert.True(violations.Count == 0, $"{DesignRule}: {string.Join("; ", violations)}");
    }

    [Fact]
    public void T131_health_checks_do_not_query_pending_migrations()
    {
        var offenders = ProductionFiles()
            .SelectMany(f => RuntimeMigrationScanner.FindReferences(File.ReadAllText(f), RuntimeMigrationScanner.PendingMigrationMembers)
                .Select(v => $"{Relative(f)}:{v.Line} usa {v.Member}"))
            .ToList();

        Assert.True(offenders.Count == 0, $"{DesignRule}; health check não consulta migrações: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void T131_scan_covers_production_sources()
    {
        Assert.NotEmpty(ProductionFiles());
    }

    [Theory]
    [InlineData("db.Database.Migrate();", "Migrate")]
    [InlineData("await db.Database.MigrateAsync(ct);", "MigrateAsync")]
    [InlineData("context.Database.EnsureCreated();", "EnsureCreated")]
    [InlineData("await ctx.Database.EnsureCreatedAsync();", "EnsureCreatedAsync")]
    [InlineData("db.Database.EnsureDeleted();", "EnsureDeleted")]
    [InlineData("await db.Database.EnsureDeletedAsync();", "EnsureDeletedAsync")]
    [InlineData("var p = db.Database.GetPendingMigrations();", "GetPendingMigrations")]
    [InlineData("var p = await db.Database.GetPendingMigrationsAsync();", "GetPendingMigrationsAsync")]
    [InlineData("db.Database\n    .Migrate ();", "Migrate")]
    [InlineData("db?.Database.Migrate();", "Migrate")]
    [InlineData("RelationalDatabaseFacadeExtensions.Migrate(db.Database);", "Migrate")]
    [InlineData("var s = $\"x {db.Database.Migrate()} y\";", "Migrate")]
    public void T131_detector_flags_forbidden_invocations(string code, string member)
    {
        var found = RuntimeMigrationScanner.FindInvocations(code);
        Assert.Contains(found, v => v.Member == member);
    }

    [Theory]
    [InlineData("// db.Database.Migrate();")]
    [InlineData("/* db.Database.EnsureCreated(); */")]
    [InlineData("/// <summary>chama Migrate() no boot</summary>")]
    [InlineData("var s = \"db.Database.Migrate()\";")]
    [InlineData("var s = @\"db.Database.EnsureCreated()\r\n.Migrate()\";")]
    [InlineData("var s = \"\"\"\r\n  db.Database.Migrate();\r\n  \"\"\";")]
    [InlineData("var s = $\"texto .Migrate() {{ .EnsureCreated() }}\";")]
    [InlineData("var c = '\"'; var s = \"a\"; // .Migrate()")]
    [InlineData("void Migrate() { }")]
    [InlineData("var x = nameof(DbContext.Database.Migrate);")]
    [InlineData("var migrated = list.MigrateLegacy();")]
    public void T131_detector_ignores_comments_strings_and_declarations(string code)
    {
        Assert.Empty(RuntimeMigrationScanner.FindInvocations(code));
    }

    [Fact]
    public void T131_detector_reports_the_line_of_the_violation()
    {
        var found = RuntimeMigrationScanner.FindInvocations("a();\n// c\nvar s = \"x\ny\";\ndb.Database.Migrate();");
        var v = Assert.Single(found);
        Assert.Equal(5, v.Line);
    }

    [Fact]
    public void T131_forbidden_members_are_declared_once_as_data()
    {
        Assert.Equal(8, RuntimeMigrationScanner.ForbiddenMembers.Count);
        Assert.Equal(RuntimeMigrationScanner.ForbiddenMembers.Count, RuntimeMigrationScanner.ForbiddenMembers.Distinct().Count());
    }

    private static List<string> ProductionFiles() =>
        SolutionLayout.ProductionProjects().SelectMany(p => p.SourceFiles).ToList();

    private static string Relative(string file) =>
        Path.GetRelativePath(SolutionLayout.BackendDirectory, file).Replace('\\', '/');
}
