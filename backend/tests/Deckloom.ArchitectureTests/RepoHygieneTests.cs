using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Deckloom.ArchitectureTests;

/// <summary>
/// Higiene do repositório (T-004; T-005 acrescenta regras de infraestrutura nesta classe).
/// Verificação estática de arquivos; nunca lê o conteúdo de arquivos .env.
/// </summary>
public class RepoHygieneTests
{
    private static readonly string[] RequiredIgnoreEntries = [".env", "bin/", "obj/", "node_modules/"];

    private static readonly string[] RequiredBinaryExtensions = ["png", "jpg", "ico", "woff2"];

    [Fact]
    public void T004_root_editorconfig_exists()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot(), ".editorconfig")), "Falta .editorconfig na raiz.");
    }

    [Fact]
    public void T004_root_gitignore_ignores_secrets_and_build_output()
    {
        var path = Path.Combine(RepoRoot(), ".gitignore");
        Assert.True(File.Exists(path), "Falta .gitignore na raiz.");

        var lines = File.ReadAllLines(path)
            .Select(l => l.Trim().TrimStart('/').Replace("**/", string.Empty))
            .ToHashSet(StringComparer.Ordinal);

        var missing = RequiredIgnoreEntries
            .Where(e => !lines.Contains(e) && !lines.Contains(e.TrimEnd('/')))
            .ToList();
        Assert.True(missing.Count == 0, $".gitignore sem as entradas: {string.Join(", ", missing)}");
    }

    [Fact]
    public void T004_no_env_file_is_tracked_by_git()
    {
        var tracked = GitLsFiles();
        var envFiles = tracked
            .Where(f => Path.GetFileName(f) is ".env" || Path.GetFileName(f).StartsWith(".env.", StringComparison.Ordinal))
            .Where(f => !f.EndsWith(".env.example", StringComparison.Ordinal))
            .ToList();
        Assert.True(envFiles.Count == 0, $"Arquivos .env rastreados: {string.Join(", ", envFiles)}");
    }

    [Fact]
    public void T004_no_build_output_is_tracked_by_git()
    {
        var offenders = GitLsFiles()
            .Where(f => f.Split('/').Any(p => p is "bin" or "obj" or "node_modules"))
            .Take(5)
            .ToList();
        Assert.True(offenders.Count == 0, $"Saída de build rastreada: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void T004_dotnet_analyzers_are_enabled_in_directory_build_props()
    {
        var props = Path.Combine(RepoRoot(), "backend", "Directory.Build.props");
        var values = XDocument.Load(props).Descendants()
            .GroupBy(e => e.Name.LocalName)
            .ToDictionary(g => g.Key, g => g.Last().Value.Trim());

        Assert.True(values.TryGetValue("EnableNETAnalyzers", out var enable) && enable == "true",
            "EnableNETAnalyzers deve ser true.");
        Assert.True(values.TryGetValue("AnalysisLevel", out var level) && level.Length > 0,
            "AnalysisLevel deve ser definido.");
        Assert.True(values.TryGetValue("EnforceCodeStyleInBuild", out var style) && style == "true",
            "EnforceCodeStyleInBuild deve ser true.");
    }

    [Fact]
    public void T004_gitattributes_forces_lf_line_endings()
    {
        var path = Path.Combine(RepoRoot(), ".gitattributes");
        Assert.True(File.Exists(path), "Falta .gitattributes na raiz (checkout com CRLF quebra dotnet format/Prettier).");

        var rules = File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        var catchAll = rules.FirstOrDefault(r => r[0] == "*");
        Assert.True(catchAll is not null && catchAll.Contains("text=auto") && catchAll.Contains("eol=lf"),
            "Falta a regra '* text=auto eol=lf'.");

        var missing = RequiredBinaryExtensions
            .Where(ext => !rules.Any(r => r[0] == $"*.{ext}" && r.Contains("binary")))
            .ToList();
        Assert.True(missing.Count == 0, $".gitattributes sem 'binary' para: {string.Join(", ", missing)}");
    }

    // ---------------------------------------------------------------- T-005
    // Valores do design.md ("Security headers" e "Configuration"); teste estático, nada é executado.

    private const string DesignCsp =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data:; connect-src 'self'; " +
        "frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'";

    private const string DesignPermissionsPolicy = "camera=(), microphone=(), geolocation=()";

    // ConnectionStrings:Deckloom fica de fora: o compose a monta a partir de DECKLOOM_APP_DB_PASSWORD (ADR-0015, DB10).
    private static readonly string[] DesignConfigNames =
    [
        "Auth:PublicOrigin", "Auth:Google:ClientId", "Auth:Google:ClientSecret", "Auth:RateLimitHmacKey",
        "Email:Smtp:Host", "Email:Smtp:Port", "Email:Smtp:UseTls",
        "Email:Smtp:User", "Email:Smtp:Password", "Email:From",
    ];

    private static readonly string[] DbPasswordVariables =
        ["POSTGRES_PASSWORD", "DECKLOOM_APP_DB_PASSWORD", "DECKLOOM_MIGRATOR_DB_PASSWORD"];

    private const string RolesScriptRelativePath = "db/init/01-roles.sh";

    private static readonly string[] RootUsers = ["root", "0"];

    private static readonly string[] SecurityHeaderNames =
        ["Content-Security-Policy", "Referrer-Policy", "X-Content-Type-Options", "Permissions-Policy"];

    private static readonly string[] SecretNameMarkers = ["Secret", "Password", "HmacKey", "ConnectionStrings"];

    [Fact]
    public void T005_compose_defines_exactly_web_api_db_and_mail()
    {
        var services = ComposeServices();
        Assert.Equal(["api", "db", "mail", "web"], services.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void T005_db_is_postgresql_17_with_a_named_volume()
    {
        var db = ComposeServices()["db"];
        Assert.Matches(@"^image:\s*postgres:17(\.\d+)*(-[\w.]+)?\s*$", ImageOf(db));
        Assert.True(MountedNamedVolumes(db).Count > 0, "O serviço db precisa de um volume nomeado para os dados.");
    }

    [Fact]
    public void T005_mail_is_mailpit()
    {
        Assert.Matches(@"^image:\s*[""']?axllent/mailpit:(?!latest\b)[\w.-]+[""']?\s*$", ImageOf(ComposeServices()["mail"]));
    }

    [Fact]
    public void T005_api_has_a_volume_for_data_protection_keys()
    {
        var services = ComposeServices();
        var dbVolumes = MountedNamedVolumes(services["db"]);
        var apiOnly = MountedNamedVolumes(services["api"]).Except(dbVolumes).ToList();
        Assert.True(apiOnly.Count > 0, "O serviço api precisa de um volume nomeado próprio (chaves do Data Protection).");

        var text = File.ReadAllText(Path.Combine(RepoRoot(), "docker-compose.yml"));
        Assert.Matches(@"(?m)^volumes:\s*$", text);
        Assert.All(apiOnly, v => Assert.Matches($@"(?m)^  {Regex.Escape(v)}:", text));
    }

    [Fact]
    public void T005_api_and_web_share_one_origin_through_the_api_proxy()
    {
        var services = ComposeServices();
        Assert.True(services["web"].Any(l => l.TrimStart().StartsWith("ports:", StringComparison.Ordinal)),
            "web deve publicar a porta de entrada.");
        Assert.False(services["api"].Any(l => l.TrimStart().StartsWith("ports:", StringComparison.Ordinal)),
            "api não pode publicar porta própria (outra origem quebraria cookies __Host-); use expose.");

        var nginx = ReadNginx();
        Assert.Matches(@"location\s+/api[/\s{]", nginx);
        Assert.Matches(@"proxy_pass\s+http://api(:\d+)?", nginx);
    }

    [Fact]
    public void T005_env_example_has_only_placeholders_for_secrets()
    {
        var entries = EnvExampleEntries();
        var offenders = entries
            .Where(e => SecretNameMarkers.Any(m => e.Name.Contains(m, StringComparison.OrdinalIgnoreCase)))
            .Where(e => e.Value.Length > 0 && !IsPlaceholder(e.Value))
            .Select(e => e.Name)
            .ToList();
        Assert.True(offenders.Count == 0, $".env.example com valor não-placeholder em: {string.Join(", ", offenders)}");

        var secretLooking = entries
            .Where(e => Regex.IsMatch(e.Value, @"GOCSPX-|AIza[\w-]{10,}|^[A-Za-z0-9+/=_-]{32,}$") && !IsPlaceholder(e.Value))
            .Select(e => e.Name)
            .ToList();
        Assert.True(secretLooking.Count == 0, $".env.example com valor que parece segredo em: {string.Join(", ", secretLooking)}");
    }

    [Fact]
    public void T005_env_example_lists_the_design_configuration_names()
    {
        var rawNames = EnvExampleEntries().Select(e => e.Name).ToList();
        var colonForm = rawNames.Where(n => n.Contains(':', StringComparison.Ordinal)).ToList();
        Assert.True(colonForm.Count == 0, $".env.example deve usar só a forma '__': {string.Join(", ", colonForm)}");

        var expected = ExpectedEnvExampleNames();
        var names = rawNames.ToHashSet(StringComparer.Ordinal);
        var missing = expected.Where(n => !names.Contains(n)).ToList();
        Assert.True(missing.Count == 0, $".env.example sem: {string.Join(", ", missing)}");

        var unexpected = names.Where(n => !expected.Contains(n)).ToList();
        Assert.True(unexpected.Count == 0, $".env.example com variáveis fora do esperado: {string.Join(", ", unexpected)}");
        Assert.Equal(rawNames.Count, names.Count);
    }

    [Fact]
    public void T005_env_example_has_placeholders_for_the_three_database_passwords()
    {
        var entries = EnvExampleEntries().ToDictionary(e => e.Name, e => e.Value, StringComparer.Ordinal);
        foreach (var name in DbPasswordVariables)
        {
            Assert.True(entries.TryGetValue(name, out var value), $".env.example sem {name}.");
            Assert.True(value!.Length == 0 || IsPlaceholder(value), $"{name} deve ser vazio ou placeholder explícito.");
        }
        Assert.DoesNotContain("ConnectionStrings__Deckloom", entries.Keys);
    }

    [Fact]
    public void T005_every_compose_variable_is_declared_in_env_example()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "docker-compose.yml"));
        var referenced = Regex.Matches(text, @"\$\{([A-Za-z_]\w*)[^}]*\}").Select(m => m.Groups[1].Value).ToHashSet();
        var declared = EnvExampleEntries().Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var missing = referenced.Where(v => !declared.Contains(v)).OrderBy(v => v, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"Variáveis do compose ausentes de .env.example: {string.Join(", ", missing)}");
    }

    // ------------------------------------------------ T-005: serviço db (ADR-0015, DB10)

    [Fact]
    public void T005_db_bootstraps_as_postgres_with_scram_and_passwords_by_variable()
    {
        var env = ComposeEnv(ComposeServices()["db"]);
        Assert.Equal("postgres", env.GetValueOrDefault("POSTGRES_USER"));
        Assert.Equal("deckloom", env.GetValueOrDefault("POSTGRES_DB"));
        Assert.Equal("scram-sha-256", env.GetValueOrDefault("POSTGRES_HOST_AUTH_METHOD"));
        foreach (var name in DbPasswordVariables)
            Assert.Equal($"${{{name}}}", env.GetValueOrDefault(name));
    }

    [Fact]
    public void T005_role_passwords_exist_only_in_the_db_service_environment()
    {
        var services = ComposeServices();
        foreach (var svc in services.Where(s => s.Key != "db"))
        {
            var env = ComposeEnv(svc.Value);
            var leaked = env.Keys.Where(k => k.StartsWith("POSTGRES_", StringComparison.Ordinal)
                || k.StartsWith("DECKLOOM_MIGRATOR_", StringComparison.Ordinal)
                || k == "DECKLOOM_APP_DB_PASSWORD").ToList();
            Assert.True(leaked.Count == 0, $"{svc.Key} define variáveis do bootstrap do banco: {string.Join(", ", leaked)}");
        }
    }

    [Fact]
    public void T005_db_mounts_init_dir_read_only_and_publishes_no_ports()
    {
        var db = ComposeServices()["db"];
        var items = db.Select(l => l.Trim()).Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l[2..].Trim().Trim('"', '\'')).ToList();
        Assert.Contains("./db/init:/docker-entrypoint-initdb.d:ro", items);
        Assert.DoesNotContain(db, l => l.TrimStart().StartsWith("ports:", StringComparison.Ordinal));
    }

    // ------------------------------------------------ T-005: db/init/01-roles.sh

    [Fact]
    public void T005_roles_script_is_strict_lf_and_requires_the_three_passwords()
    {
        var raw = File.ReadAllText(RolesScriptPath());
        Assert.DoesNotContain('\r', raw);

        var code = ShellCode(raw);
        Assert.Matches(@"(?m)^\s*set -euo pipefail\s*$", code);
        foreach (var name in DbPasswordVariables)
            Assert.Contains($"${{{name}:?", code, StringComparison.Ordinal);
    }

    [Fact]
    public void T005_roles_script_passes_passwords_to_psql_as_variables_in_a_quoted_heredoc()
    {
        var code = ShellCode(File.ReadAllText(RolesScriptPath()));
        Assert.Matches(@"\bpsql\b", code);
        Assert.Matches(@"-v\s+ON_ERROR_STOP=1", code);
        Assert.Matches(@"-v\s+app_password=[""']?\$\{?DECKLOOM_APP_DB_PASSWORD", code);
        Assert.Matches(@"-v\s+migrator_password=[""']?\$\{?DECKLOOM_MIGRATOR_DB_PASSWORD", code);
        Assert.Matches(@"<<-?\s*'SQL'", code);
        Assert.Contains(":'app_password'", code, StringComparison.Ordinal);
        Assert.Contains(":'migrator_password'", code, StringComparison.Ordinal);
    }

    [Fact]
    public void T005_roles_script_contains_no_literal_password()
    {
        var sql = Sql();
        Assert.DoesNotMatch(@"PASSWORD\s+E?'", sql);
        Assert.DoesNotMatch(@"PASSWORD\s+""", sql);
        Assert.Matches(@"PASSWORD\s+:'app_password'", sql);
        Assert.Matches(@"PASSWORD\s+:'migrator_password'", sql);
    }

    [Fact]
    public void T005_roles_script_turns_statement_logging_off_before_creating_roles()
    {
        var sql = Sql();
        var off = Regex.Match(sql, @"SET\s+log_statement\s*(=|TO)\s*'none'", RegexOptions.IgnoreCase);
        var firstCreate = Regex.Match(sql, @"CREATE\s+ROLE", RegexOptions.IgnoreCase);
        Assert.True(off.Success, "Falta SET log_statement = 'none'.");
        Assert.True(firstCreate.Success, "Falta CREATE ROLE.");
        Assert.True(off.Index < firstCreate.Index, "SET log_statement = 'none' deve vir antes do primeiro CREATE ROLE.");
    }

    [Fact]
    public void T005_roles_script_makes_migrator_a_set_only_member_of_owner()
    {
        var grant = Regex.Match(Sql(), @"GRANT\s+deckloom_owner\s+TO\s+deckloom_migrator\b([^;]*);", RegexOptions.IgnoreCase);
        Assert.True(grant.Success, "Falta GRANT deckloom_owner TO deckloom_migrator.");
        Assert.Matches(@"(?i)INHERIT\s+FALSE", grant.Groups[1].Value);
        Assert.Matches(@"(?i)SET\s+TRUE", grant.Groups[1].Value);
        Assert.DoesNotMatch(@"(?i)\bADMIN\b", grant.Groups[1].Value);
    }

    // R-11 (UA4): o motivo de o bootstrap ser .sh e não .sql fica registrado no cabeçalho do script.
    [Fact]
    public void T005_roles_script_header_explains_why_it_is_sh_and_not_sql_ua4()
    {
        var header = File.ReadAllLines(RolesScriptPath())
            .TakeWhile(l => l.StartsWith('#') || string.IsNullOrWhiteSpace(l))
            .ToList();
        var text = string.Join('\n', header);
        Assert.True(text.Contains("UA4", StringComparison.Ordinal),
            "O cabeçalho de db/init/01-roles.sh deve citar UA4 e explicar por que o bootstrap é .sh e não .sql.");
        Assert.Matches(@"\.sql\b", text);
    }

    // R-12 (design "Logs"): o error_log padrão (nível error) grava a linha da requisição, com code/state do OAuth e IP.
    [Fact]
    public void T005_nginx_error_log_does_not_record_request_lines()
    {
        var nginx = Regex.Replace(ReadNginx(), @"#[^\n]*", string.Empty);
        var directives = Regex.Matches(nginx, @"(?m)^\s*error_log\s+(\S+)(?:\s+(\w+))?\s*;")
            .Select(m => (Target: m.Groups[1].Value, Level: m.Groups[2].Success ? m.Groups[2].Value : "error"))
            .ToList();
        Assert.True(directives.Count > 0,
            "frontend/nginx.conf sem error_log explícito: o padrão (nível error) grava a linha da requisição.");
        var tooVerbose = directives.Where(d => d.Level is not ("crit" or "alert" or "emerg")).ToList();
        Assert.True(tooVerbose.Count == 0,
            $"error_log deve ser crit ou mais restrito: {string.Join(", ", tooVerbose.Select(d => $"{d.Target} {d.Level}"))}");
    }

    [Fact]
    public void T005_roles_script_revokes_public_privileges_and_grants_connect_only_to_login_roles()
    {
        var sql = Sql();
        Assert.Matches(@"(?i)REVOKE\s+ALL(\s+PRIVILEGES)?\s+ON\s+DATABASE\s+deckloom\s+FROM\s+PUBLIC\s*;", sql);
        Assert.Matches(@"(?i)REVOKE\s+ALL(\s+PRIVILEGES)?\s+ON\s+SCHEMA\s+public\s+FROM\s+PUBLIC\s*;", sql);
        Assert.Matches(
            @"(?i)ALTER\s+DEFAULT\s+PRIVILEGES\s+FOR\s+ROLE\s+deckloom_owner\s+REVOKE\s+EXECUTE\s+ON\s+FUNCTIONS\s+FROM\s+PUBLIC\s*;",
            sql);
        Assert.Matches(@"(?i)GRANT\s+CONNECT\s+ON\s+DATABASE\s+deckloom\s+TO\s+[^;]*\bdeckloom_app\b", sql);
        Assert.Matches(@"(?i)GRANT\s+CONNECT\s+ON\s+DATABASE\s+deckloom\s+TO\s+[^;]*\bdeckloom_migrator\b", sql);
        Assert.Matches(@"(?i)GRANT\s+CREATE\s+ON\s+DATABASE\s+deckloom\s+TO\s+deckloom_owner\s*;", sql);
    }

    [Fact]
    public void T005_roles_script_creates_no_schema_or_table_and_grants_nothing_on_tables()
    {
        var sql = Sql();
        Assert.DoesNotMatch(@"(?i)CREATE\s+(SCHEMA|TABLE)\b", sql);
        Assert.DoesNotMatch(@"(?i)GRANT\s+[^;]*\bON\s+(ALL\s+TABLES|TABLE\b)", sql);
        Assert.DoesNotMatch(@"(?i)GRANT\s+[^;]*\bON\s+SCHEMA\s+(accounts|deck_suggestion)\b", sql);
    }

    // ------------------------------------------------ T-005: serviço api (ADR-0015, DB10)

    [Fact]
    public void T005_api_connects_to_the_database_as_deckloom_app_with_the_design_connection_string()
    {
        var env = ComposeEnv(ComposeServices()["api"]);
        Assert.Equal("Development", env.GetValueOrDefault("ASPNETCORE_ENVIRONMENT"));
        Assert.True(env.TryGetValue("ConnectionStrings__Deckloom", out var cs), "api sem ConnectionStrings__Deckloom.");

        var parts = cs!.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0].Trim(), p => p.Length > 1 ? p[1].Trim() : string.Empty, StringComparer.OrdinalIgnoreCase);

        Assert.Equal("db", parts.GetValueOrDefault("Host"));
        Assert.Equal("deckloom", parts.GetValueOrDefault("Database"));
        Assert.Equal("deckloom_app", parts.GetValueOrDefault("Username"));
        Assert.Equal("${DECKLOOM_APP_DB_PASSWORD}", parts.GetValueOrDefault("Password"));
        Assert.Equal("Disable", parts.GetValueOrDefault("SSL Mode"));
        Assert.Equal("20", parts.GetValueOrDefault("Maximum Pool Size"));
        Assert.Equal("5", parts.GetValueOrDefault("Command Timeout"));
        Assert.Equal("5", parts.GetValueOrDefault("Timeout"));
        Assert.Equal("deckloom-api", parts.GetValueOrDefault("Application Name"));

        Assert.DoesNotMatch(@"(?i)postgres|deckloom_migrator|deckloom_owner", cs);
    }

    // Legal:* e obrigatoria na inicializacao (LegalOptionsValidator); sem ela a API do compose nao sobe.
    [Theory]
    [InlineData("Legal__TermsVersion")]
    [InlineData("Legal__TermsUrl")]
    [InlineData("Legal__PrivacyUrl")]
    public void Api_service_defines_the_legal_settings_required_at_startup(string name)
    {
        var env = ComposeEnv(ComposeServices()["api"]);
        Assert.True(env.TryGetValue(name, out var value) && value.Length > 0, $"api sem {name}.");
    }

    [Fact]
    public void T005_api_service_has_no_bootstrap_or_migrator_variables_or_references()
    {
        var api = ComposeServices()["api"];
        Assert.DoesNotContain(ComposeEnv(api).Keys, k =>
            k.StartsWith("POSTGRES_", StringComparison.Ordinal) || k.StartsWith("DECKLOOM_MIGRATOR_", StringComparison.Ordinal));
        var text = string.Join('\n', api);
        Assert.DoesNotContain("${POSTGRES_", text, StringComparison.Ordinal);
        Assert.DoesNotContain("${DECKLOOM_MIGRATOR_", text, StringComparison.Ordinal);
    }

    // R-31: web na rede default faria o DNS resolver "api" fora da sub-rede confiavel (edge), e a API
    // ignoraria o X-Forwarded-For. web fica somente em edge; api fica em edge e default (db e mail).
    [Fact]
    public void T005_web_is_attached_only_to_edge_and_api_to_edge_and_default()
    {
        var services = ComposeServices();
        Assert.Equal(["edge"], ServiceNetworks(services["web"]));
        Assert.Equal(["default", "edge"], ServiceNetworks(services["api"]).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void T005_gitignore_ignores_env_but_keeps_the_example_trackable()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), ".gitignore")).Select(l => l.Trim()).ToList();
        Assert.Contains(lines, l => l is ".env" or "/.env" or "**/.env");
        var broadIgnore = lines.Any(l => l is ".env*" or ".env.*");
        Assert.False(broadIgnore && !lines.Contains("!.env.example"), ".gitignore ignora .env.example sem exceção.");
    }

    [Fact]
    public void T005_nginx_sends_the_design_security_headers()
    {
        var headers = NginxHeaders(ReadNginx());
        Assert.Equal(DesignCsp, headers.GetValueOrDefault("Content-Security-Policy"));
        Assert.Equal("no-referrer", headers.GetValueOrDefault("Referrer-Policy"));
        Assert.Equal("nosniff", headers.GetValueOrDefault("X-Content-Type-Options"));
        Assert.Equal(DesignPermissionsPolicy, headers.GetValueOrDefault("Permissions-Policy"));
    }

    [Fact]
    public void T005_nginx_security_headers_are_sent_on_every_response()
    {
        var nginx = ReadNginx();
        foreach (var name in SecurityHeaderNames)
            Assert.Matches($@"add_header\s+{name}\s+""[^""]*""\s+always\s*;", nginx);
    }

    // Upload multipart de avatar: o endpoint recusa acima de 2,5 MB (Http:Limits:MaxUploadBytes, padrao 2621440, design.md).
    // O nginx tem 1 MB por padrao e barraria com 413 sem corpo o que a API aceitaria; o limite do /api acompanha o da API.
    private const long ApiUploadLimitBytes = 2_621_440;

    [Fact]
    public void T005_nginx_api_location_allows_bodies_up_to_the_api_upload_limit()
    {
        var nginx = Regex.Replace(ReadNginx(), @"#[^\n]*", string.Empty);
        var apiBlock = Regex.Match(nginx, @"location\s+/api\b[^{]*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
        Assert.True(apiBlock.Success, "frontend/nginx.conf sem location /api.");

        var directive = Regex.Match(
            apiBlock.Groups["body"].Value, @"(?m)^\s*client_max_body_size\s+(?<n>\d+)(?<unit>[kKmM]?)\s*;");
        Assert.True(directive.Success, "location /api sem client_max_body_size (o padrao do nginx e 1 MB).");

        var bytes = long.Parse(directive.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture)
            * directive.Groups["unit"].Value.ToLowerInvariant() switch { "k" => 1024L, "m" => 1024L * 1024, _ => 1L };
        Assert.Equal(ApiUploadLimitBytes, bytes);
    }

    // SEC-02: a chave de IP (atraso progressivo e limitador de concorrencia) sai de um unico ponto, ClientIpKey, que
    // trunca IPv6 em /64. Ler RemoteIpAddress em outro lugar reabre o desvio por troca de endereco dentro do /64.
    [Fact]
    public void SEC02_only_ClientIpKey_reads_the_remote_ip_address_in_the_api()
    {
        var apiRoot = Path.Combine(RepoRoot(), "backend", "src", "Deckloom.Api");
        var offenders = Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Path.GetFileName(f) != "ClientIpKey.cs")
            .Where(f => Regex.IsMatch(Regex.Replace(File.ReadAllText(f), @"//[^\n]*", string.Empty), @"\bRemoteIpAddress\b"))
            .Select(f => Path.GetRelativePath(apiRoot, f))
            .ToList();
        Assert.True(offenders.Count == 0, $"RemoteIpAddress lido fora de ClientIpKey: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void T005_api_dockerfile_runs_as_non_root()
    {
        var path = Path.Combine(RepoRoot(), "backend", "Dockerfile");
        Assert.True(File.Exists(path), "Falta backend/Dockerfile.");
        var lastUser = File.ReadAllLines(path).Select(l => l.Trim())
            .Where(l => l.StartsWith("USER ", StringComparison.OrdinalIgnoreCase))
            .Select(l => l[5..].Trim().Split(':')[0])
            .LastOrDefault();
        Assert.False(string.IsNullOrEmpty(lastUser), "Dockerfile da API sem instrução USER.");
        Assert.DoesNotContain(lastUser!, RootUsers);
    }

    [Fact]
    public void T005_image_versions_are_pinned_in_compose_and_dockerfiles()
    {
        var unpinned = new List<string>();
        foreach (var svc in ComposeServices())
        {
            var image = ImageOf(svc.Value);
            if (image.Length > 0 && !IsPinned(image["image:".Length..].Trim().Trim('"', '\'')))
                unpinned.Add($"compose:{svc.Key}");
        }

        foreach (var rel in new[] { "backend/Dockerfile", "frontend/Dockerfile" })
        {
            var full = Path.Combine(RepoRoot(), rel);
            Assert.True(File.Exists(full), $"Falta {rel}.");
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fromLines = File.ReadAllLines(full).Select(l => l.Trim())
                .Where(l => l.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase));
            foreach (var line in fromLines)
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => !p.StartsWith("--", StringComparison.Ordinal)).ToArray();
                var image = parts[1];
                if (!aliases.Contains(image) && !IsPinned(image))
                    unpinned.Add($"{rel}:{image}");
                if (parts.Length >= 4 && parts[2].Equals("AS", StringComparison.OrdinalIgnoreCase))
                    aliases.Add(parts[3]);
            }
        }
        Assert.True(unpinned.Count == 0, $"Imagens sem versão fixa: {string.Join(", ", unpinned)}");
    }

    private static bool IsPinned(string image)
    {
        if (image.Contains('$', StringComparison.Ordinal)) return false;
        if (image.Contains("@sha256:", StringComparison.Ordinal)) return true;
        var name = image[(image.LastIndexOf('/') + 1)..];
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && name[(colon + 1)..] is { Length: > 0 } tag
            && !tag.Equals("latest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaceholder(string value) =>
        Regex.IsMatch(
            value.Trim('"', '\''),
            @"^(<[^>]+>|\$\{[^}]+\}|(change-?me|replace-?me|your-|placeholder|example|set-)[\w.-]*)$",
            RegexOptions.IgnoreCase);

    private static HashSet<string> ExpectedEnvExampleNames() =>
        DesignConfigNames.Select(n => n.Replace(":", "__", StringComparison.Ordinal))
            .Concat(DbPasswordVariables)
            .ToHashSet(StringComparer.Ordinal);

    private static string RolesScriptPath()
    {
        var path = Path.Combine(RepoRoot(), RolesScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Falta {RolesScriptRelativePath}.");
        return path;
    }

    /// <summary>Script sem linhas de comentário (# ou --), com quebras de linha preservadas.</summary>
    private static string ShellCode(string raw) =>
        string.Join('\n', raw.Split('\n').Where(l =>
            !l.TrimStart().StartsWith('#') && !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    /// <summary>Código do script com espaços em branco colapsados, para casar instruções SQL em várias linhas.</summary>
    private static string Sql() =>
        Regex.Replace(ShellCode(File.ReadAllText(RolesScriptPath())), @"\s+", " ");

    /// <summary>Variáveis do bloco "environment:" de um serviço (forma mapa "K: v" ou lista "- K=v").</summary>
    private static Dictionary<string, string> ComposeEnv(List<string> service)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var inEnv = false;
        foreach (var raw in service)
        {
            if (Regex.IsMatch(raw, @"^    environment:\s*$")) { inEnv = true; continue; }
            if (!inEnv) continue;
            if (!Regex.IsMatch(raw, @"^      \S")) { inEnv = false; continue; }
            var line = raw.Trim();
            var m = Regex.Match(line, @"^([A-Za-z_]\w*):\s*(.*)$");
            if (!m.Success) m = Regex.Match(line, @"^-\s*([A-Za-z_]\w*)=(.*)$");
            if (m.Success) result[m.Groups[1].Value] = m.Groups[2].Value.Trim().Trim('"', '\'');
        }
        return result;
    }

    /// <summary>Redes do bloco "networks:" (lista) de um serviço.</summary>
    private static string[] ServiceNetworks(List<string> service)
    {
        var result = new List<string>();
        var inNetworks = false;
        foreach (var raw in service)
        {
            if (Regex.IsMatch(raw, @"^    networks:\s*$")) { inNetworks = true; continue; }
            if (!inNetworks) continue;
            var m = Regex.Match(raw, @"^      -\s*(\S+)\s*$");
            if (m.Success) result.Add(m.Groups[1].Value);
            else inNetworks = false;
        }
        return [.. result];
    }

    private static List<(string Name, string Value)> EnvExampleEntries()
    {
        var path = Path.Combine(RepoRoot(), ".env.example");
        Assert.True(File.Exists(path), "Falta .env.example na raiz.");
        return File.ReadAllLines(path).Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#') && l.Contains('='))
            .Select(l => (Name: l[..l.IndexOf('=')].Trim(), Value: l[(l.IndexOf('=') + 1)..].Trim()))
            .ToList();
    }

    private static string ReadNginx()
    {
        var path = Path.Combine(RepoRoot(), "frontend", "nginx.conf");
        Assert.True(File.Exists(path), "Falta frontend/nginx.conf.");
        return File.ReadAllText(path);
    }

    private static Dictionary<string, string> NginxHeaders(string nginx) =>
        Regex.Matches(nginx, @"add_header\s+([\w-]+)\s+""([^""]*)""")
            .GroupBy(m => m.Groups[1].Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Groups[2].Value, StringComparer.OrdinalIgnoreCase);

    private static string ImageOf(List<string> service) =>
        service.Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("image:", StringComparison.Ordinal)) ?? string.Empty;

    private static List<string> MountedNamedVolumes(List<string> service) =>
        service.Select(l => l.Trim())
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l[2..].Trim().Trim('"', '\''))
            .Where(l => Regex.IsMatch(l, @"^[A-Za-z][\w.-]*:/"))
            .Select(l => l[..l.IndexOf(':', StringComparison.Ordinal)])
            .ToList();

    /// <summary>Divide o bloco top-level "services:" do compose em serviços (chave com 2 espaços de indentação).</summary>
    private static Dictionary<string, List<string>> ComposeServices()
    {
        var path = Path.Combine(RepoRoot(), "docker-compose.yml");
        Assert.True(File.Exists(path), "Falta docker-compose.yml na raiz.");
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var inServices = false;
        List<string>? current = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            if (raw.Length == 0 || raw.TrimStart().StartsWith('#')) continue;
            if (!char.IsWhiteSpace(raw[0]))
            {
                inServices = raw.TrimEnd() == "services:";
                current = null;
                continue;
            }
            if (!inServices) continue;
            var m = Regex.Match(raw, @"^  ([A-Za-z][\w.-]*):\s*$");
            if (m.Success) { current = []; result[m.Groups[1].Value] = current; }
            else current?.Add(raw);
        }
        return result;
    }

    // T-153 (SEC-17): toda porta publicada do compose fica so no loopback do host; a leitura e estatica (sem docker).
    [Fact]
    public void T153_every_published_port_binds_to_loopback_only()
    {
        foreach (var (name, lines) in ComposeServices())
        {
            foreach (var entry in PublishedPorts(lines))
            {
                Assert.Matches(@"^(127\.0\.0\.1|\[::1\]):\d+:\d+(/(tcp|udp))?$", entry);
                Assert.True(name is "web" or "mail", $"O servico '{name}' nao deve publicar porta.");
            }
        }
    }

    [Fact]
    public void T153_web_and_mail_publish_exactly_the_documented_loopback_ports()
    {
        var services = ComposeServices();
        Assert.Equal(["127.0.0.1:8081:80"], PublishedPorts(services["web"]));
        Assert.Equal(["127.0.0.1:8025:8025"], PublishedPorts(services["mail"]));
    }

    [Fact]
    public void T153_api_and_db_publish_no_port()
    {
        var services = ComposeServices();
        Assert.Empty(PublishedPorts(services["api"]));
        Assert.Empty(PublishedPorts(services["db"]));
    }

    // QA (fase 6): formas que contornariam a leitura da lista "ports:" acima sem falhar T153: lista em linha
    // ("ports: [...]"), rede do host, "privileged"/"pid" do host e outro arquivo de compose (override) que publicaria
    // portas fora do loopback. So arquivos versionados contam (git ls-files): um docker-compose.override.yml local,
    // nao rastreado, nao reprova a suite de quem a roda; ao ser commitado, reprova.
    [Fact]
    public void QA_compose_has_no_inline_ports_no_host_network_no_privileged_or_host_pid_and_no_tracked_override_files()
    {
        foreach (var (name, lines) in ComposeServices())
        {
            Assert.False(
                lines.Exists(l => Regex.IsMatch(l.Trim(), @"^ports:\s*\S")),
                $"O servico '{name}' usa lista de portas em linha.");
            Assert.False(
                lines.Exists(l => Regex.IsMatch(l.Trim(), @"^network_mode:\s*[""']?host", RegexOptions.IgnoreCase)),
                $"O servico '{name}' usa network_mode: host.");
            Assert.False(
                lines.Exists(l => Regex.IsMatch(l.Trim(), @"^(privileged|pid):\s*[""']?(true|host)", RegexOptions.IgnoreCase)),
                $"O servico '{name}' usa privileged ou pid do host.");
        }

        var extras = GitLsFiles()
            .Where(f => Regex.IsMatch(f, @"(^|/)(docker-)?compose[^/]*\.ya?ml$", RegexOptions.IgnoreCase))
            .Where(f => !string.Equals(f, "docker-compose.yml", StringComparison.Ordinal))
            .ToList();
        Assert.True(extras.Count == 0, $"Arquivos de compose versionados alem de docker-compose.yml: {string.Join(", ", extras)}");
    }

    // Entradas da lista "ports:" do servico, sem aspas; so a forma curta ("ip:host:container") e esperada.
    private static List<string> PublishedPorts(List<string> service)
    {
        var result = new List<string>();
        var portsIndent = -1;
        foreach (var raw in service)
        {
            var indent = raw.Length - raw.TrimStart().Length;
            var line = raw.Trim();
            if (portsIndent >= 0)
            {
                if (indent > portsIndent && line.StartsWith("- ", StringComparison.Ordinal))
                {
                    result.Add(line[2..].Trim().Trim('"', '\''));
                    continue;
                }

                portsIndent = -1;
            }

            if (line == "ports:") portsIndent = indent;
        }

        return result;
    }

    // T-148 (R-29): a leitura limitada do corpo e a desserializacao ficam so em ReadJsonAsync; RegisterAsync a usa.
    [Fact]
    public void T148_auth_endpoints_read_the_json_body_in_a_single_place()
    {
        var path = Path.Combine(RepoRoot(), "backend", "src", "Deckloom.Api", "Auth", "AuthEndpoints.cs");
        var source = File.ReadAllText(path);

        var boundedReads = Regex.Count(source, @"ReadBoundedBodyAsync\(") - 1; // menos a declaracao
        var deserializations = Regex.Count(source, @"JsonSerializer\.Deserialize");

        Assert.True(
            boundedReads == 1,
            $"AuthEndpoints.cs tem {boundedReads} chamadas de ReadBoundedBodyAsync; esperado 1 (dentro de ReadJsonAsync).");
        Assert.True(
            deserializations == 1,
            $"AuthEndpoints.cs tem {deserializations} desserializacoes JSON; esperado 1 (dentro de ReadJsonAsync).");
        Assert.Contains("ReadJsonAsync<RegisterRequest>", source, StringComparison.Ordinal);
    }

    private static string[] GitLsFiles()
    {
        var psi = new ProcessStartInfo("git", "ls-files")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("git indisponível.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório (.git) não encontrada.");
    }
}
