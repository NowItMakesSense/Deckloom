using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Infrastructure;
using Deckloom.Accounts.Testing;
using Deckloom.DeckSuggestion.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Sha256TokenHasher = Deckloom.Accounts.Testing.Sha256TokenHasher;

namespace Deckloom.Api.Tests;

// T-093 (RED): redacao de logs ponta a ponta. Todos os fluxos da conta rodam por HTTP contra o host real com fakes
// em memoria; um provedor de log captura TUDO (mensagem, estado estruturado, escopos, excecao, categoria) em Trace.
// Nenhum valor sensivel usado nas entradas (email, senha, tokens, code/state OAuth, hashes, IP, nome, bio) pode
// aparecer; so playerId, sessionId, codigos e traceId. Os 21 eventos 1001-1021 devem ser emitidos nos fluxos.
// Lacunas assumidas (ver perguntas ao orquestrador):
// (1) o IP do cliente e definido por um IStartupFilter (RemoteIpAddress) a partir do header X-Test-Client-Ip;
// (2) o evento 1008 (PasswordResetEmailFailed) hoje so e emitido por SmtpPasswordResetSender (infra), nao pelo
//     fake IPasswordResetNotifier: o teste espera que uma falha do notificador resulte no 1008 via HTTP;
// (3) EnableSensitiveDataLogging e verificado nas opcoes dos DbContexts resolvidos do DI (nenhuma conexao e aberta);
// (4) o provedor de captura e local a este arquivo (o RecordingLoggerProvider fica noutro projeto de teste e nao
//     captura escopos nem excecoes).
public sealed class LogRedactionTests : IClassFixture<LogRedactionTests.FlowsFixture>
{
    private readonly FlowsFixture _flows;

    public LogRedactionTests(FlowsFixture flows) => _flows = flows;

    [Fact]
    public void NFR_flows_ran_far_enough_to_make_the_scan_meaningful()
    {
        Assert.True(_flows.LoggedInAsAna, "login da Ana deveria ter emitido o cookie de sessao");
        Assert.True(_flows.Secrets.Count > 20, "o teste deveria ter coletado os valores sensiveis das entradas");
        Assert.NotEmpty(_flows.Logs);
    }

    [Fact]
    public void NFR_no_log_entry_contains_any_sensitive_value_used_in_the_flows()
    {
        var leaks = new List<string>();
        foreach (var entry in _flows.Logs)
        {
            var text = entry.FullText();
            foreach (var (label, value) in _flows.Secrets)
            {
                if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
                {
                    leaks.Add($"[{entry.Category}] event {entry.EventId} ({entry.Level}) leaks <{label}>");
                }
            }
        }

        Assert.True(leaks.Count == 0, "vazamentos em logs:\n" + string.Join("\n", leaks.Distinct()));
    }

    [Fact]
    public void NFR_catalog_events_carry_only_the_allowed_fields()
    {
        var violations = new List<string>();
        foreach (var entry in _flows.Logs.Where(e => IsCatalogCandidate(e) && e.EventId is >= 1001 and <= 1021))
        {
            var allowed = AccountsLog.Catalog.Single(c => c.Id == entry.EventId).Fields;
            var actual = entry.Properties.Keys.Where(k => k != "{OriginalFormat}").ToArray();
            if (actual.Except(allowed, StringComparer.Ordinal).Any())
            {
                violations.Add($"event {entry.EventId}: campos {string.Join(",", actual)} fora de {string.Join(",", allowed)}");
            }
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void NFR_sensitive_data_logging_is_disabled_on_every_db_context()
    {
        using var scope = _flows.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AccountsDbContext>>().CreateDbContext();
        using (accounts)
        {
            Assert.False(SensitiveLoggingEnabled(accounts));
        }

        var catalog = scope.ServiceProvider.GetService<FormatCatalogDbContext>();
        if (catalog is not null)
        {
            Assert.False(SensitiveLoggingEnabled(catalog));
        }
    }

    [Theory]
    [InlineData(1001)]
    [InlineData(1002)]
    [InlineData(1003)]
    [InlineData(1004)]
    [InlineData(1005)]
    [InlineData(1006)]
    [InlineData(1007)]
    [InlineData(1009)]
    [InlineData(1010)]
    [InlineData(1011)]
    [InlineData(1012)]
    [InlineData(1013)]
    [InlineData(1014)]
    [InlineData(1015)]
    [InlineData(1016)]
    [InlineData(1017)]
    [InlineData(1018)]
    [InlineData(1019)]
    [InlineData(1020)]
    [InlineData(1021)]
    public void NFR_catalog_event_is_emitted_in_its_flow(int eventId)
    {
        var emitted = _flows.Logs.Where(e => IsCatalogCandidate(e) && e.EventId == eventId).ToArray();

        Assert.True(emitted.Length > 0, $"evento {eventId} nao foi emitido nos fluxos HTTP");
        var name = AccountsLog.Catalog.Single(c => c.Id == eventId).Name;
        Assert.All(emitted, e => Assert.Equal(name, e.EventName));
    }

    [Fact]
    public void NFR_event_1008_is_emitted_when_the_reset_email_sending_fails()
    {
        Assert.Contains(_flows.Logs, e => IsCatalogCandidate(e) && e.EventId == 1008 && e.EventName == "PasswordResetEmailFailed");
    }

    // R-68: o emissor do 1008 neste fluxo so enfileira (sem causa SMTP), entao a causa e "other", valor da lista fechada.
    [Fact]
    public void R68_event_1008_from_the_queueing_emitter_carries_the_closed_list_cause_other()
    {
        var emitted = _flows.Logs.Where(e => IsCatalogCandidate(e) && e.EventId == 1008).ToArray();

        Assert.NotEmpty(emitted);
        Assert.All(emitted, e => Assert.Equal("other", e.Properties["cause"]));
    }

    [Fact]
    public void NFR_event_1021_carries_only_sqlstate_and_operation_without_sql_parameters_host_or_connection_string()
    {
        var failed = Assert.Single(_flows.DatabaseFailureLogs, e => IsCatalogCandidate(e) && e.EventId == 1021);

        Assert.Equal("DatabaseCommandFailed", failed.EventName);
        Assert.Equal(
            ["operation", "sqlState"],
            failed.Properties.Keys.Where(k => k != "{OriginalFormat}").Order().ToArray());
        Assert.Equal(FlowsFixture.FailingSqlState, failed.Properties["sqlState"]?.ToString());
        foreach (var entry in _flows.DatabaseFailureLogs)
        {
            var text = entry.FullText();
            foreach (var forbidden in FlowsFixture.DatabaseLeakMarkers)
            {
                Assert.False(
                    text.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"[{entry.Category}] event {entry.EventId} vaza '{forbidden}' na falha de banco");
            }
        }
    }

    // Ids 1001-1021 colidem com eventos do framework (ex.: Routing 1001); o catalogo e do proprio codigo Deckloom.
    private static bool IsCatalogCandidate(LoggedEntry e) =>
        !e.Category.StartsWith("Microsoft.", StringComparison.Ordinal) &&
        !e.Category.StartsWith("System.", StringComparison.Ordinal);

    private static bool SensitiveLoggingEnabled(DbContext context) =>
        ((IInfrastructure<IServiceProvider>)context).Instance
            .GetRequiredService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?.IsSensitiveDataLoggingEnabled ?? false;

    public sealed record LoggedEntry(
        string Category,
        int EventId,
        string? EventName,
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Properties,
        IReadOnlyList<string> Scopes,
        string? Exception)
    {
        public string FullText() => string.Join(
            "\n",
            new[] { Category, Message, Exception ?? string.Empty }
                .Concat(Properties.Select(p => $"{p.Key}={p.Value}"))
                .Concat(Scopes));
    }

    public sealed class FlowsFixture : IAsyncLifetime, IDisposable
    {
        public const string FailingSqlState = "42501";

        public static readonly string[] DatabaseLeakMarkers =
        [
            "SELECT ", "FROM accounts.player", "@p0", "server detail", "db-host-synthetic.internal",
            "test-db-secret-9f3a", "Password=", "Username=", "ana@example.com", "senha-valida-123",
        ];

        private static readonly string[] StandardOnly = ["standard"];

        private const string PublicOrigin = "https://deckloom.example.test";
        private const string CsrfCookie = "__Host-dl_csrf";
        private const string SessionCookie = "__Host-dl_session";
        private const string OAuthCookie = "__Host-dl_oauth";
        private const string PendingCookie = "__Host-dl_pending";
        private const string IpHeader = "X-Test-Client-Ip";
        private const string ClientIp = "203.0.113.77";
        private const string Terms = "2026-10";
        private const string AnaEmail = "ana@example.com";
        private const string GuiEmail = "gui@example.com";
        private const string NovoEmail = "novo@example.com";
        private const string AnaPassword = "senha-valida-123";
        private const string WrongPassword = "senha-errada-999";
        private const string NewPassword = "nova-senha-456-xyz";
        private const string AnaDisplayName = "Ana Sentinela Registro";
        private const string ProfileDisplayName = "Ana Mage Sentinela";
        private const string Bio = "Bio sentinela unica do teste";
        private const string NovoDisplayName = "Novo Jogador Sentinela";

        private readonly ConcurrentQueue<LoggedEntry> _logs = new();
        private readonly List<(string Label, string Value)> _secrets = [];
        private readonly LogFactory _factory;
        private int _failureStart;
        private string _currentPassword = AnaPassword;

        public FlowsFixture() => _factory = new LogFactory(_logs);

        public IReadOnlyList<LoggedEntry> Logs => _logs.ToArray();

        public IReadOnlyList<LoggedEntry> DatabaseFailureLogs => _logs.Skip(_failureStart).ToArray();

        public IReadOnlyList<(string Label, string Value)> Secrets => _secrets;

        public IServiceProvider Services => _factory.Services;

        public bool LoggedInAsAna { get; private set; }

        public async Task InitializeAsync()
        {
            AddSecret("email ana", AnaEmail);
            AddSecret("email gui", GuiEmail);
            AddSecret("email novo", NovoEmail);
            AddSecret("senha", AnaPassword);
            AddSecret("senha errada", WrongPassword);
            AddSecret("senha nova", NewPassword);
            AddSecret("nome de exibicao do registro", AnaDisplayName);
            AddSecret("nome de exibicao do perfil", ProfileDisplayName);
            AddSecret("bio", Bio);
            AddSecret("nome de exibicao do Google novo", NovoDisplayName);
            AddSecret("nome do provedor Google", "Nome Google Sentinela");
            AddSecret("IP do cliente", ClientIp);
            AddSecret("sub Google", "sub-gui-sentinela");
            AddSecret("sub Google novo", "sub-novo-sentinela");
            AddSecret("sub Google ana", "sub-ana-sentinela");
            AddSecret("token de sessao semeado", LogFactory.SeedSessionToken);
            AddHashes("token de sessao semeado", LogFactory.SeedSessionToken);
            AddBytes("hash de IP", new FakeKeyedHasher().Hash(ClientIp));
            AddBytes("hash de conta ana", new FakeKeyedHasher().Hash(AnaEmail));
            AddBytes("hash de conta gui", new FakeKeyedHasher().Hash(GuiEmail));

            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = false,
            });

            await RunPasswordFlows(client);
            await RunGoogleFlows(client);
            await RunDeletionFlows(client);
            await RunDatabaseFailureFlow(client);
            await CollectStoredHashes();
        }

        public Task DisposeAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose() => _factory.Dispose();

        private async Task RunPasswordFlows(HttpClient client)
        {
            var register = await Post(client, "/api/v1/auth/register", new()
            {
                ["email"] = AnaEmail,
                ["password"] = AnaPassword,
                ["displayName"] = AnaDisplayName,
                ["acceptTerms"] = true,
                ["termsVersion"] = Terms,
                ["adultDeclaration"] = true,
            });

            // login falho (senha errada e email desconhecido), CSRF rejeitado, sucesso
            await Post(client, "/api/v1/auth/login", Login(AnaEmail, WrongPassword));
            await Post(client, "/api/v1/auth/login", Login("ninguem@example.com", WrongPassword));
            await PostWithoutCsrf(client, "/api/v1/auth/login", Login(AnaEmail, AnaPassword));
            var login = await Post(client, "/api/v1/auth/login", Login(AnaEmail, AnaPassword));
            var session = SessionCookieOf(login) ?? SessionCookieOf(register);
            LoggedInAsAna = session is not null;
            session ??= $"{SessionCookie}={LogFactory.SeedSessionToken}";

            await Put(client, "/api/v1/me/profile", new()
            {
                ["displayName"] = ProfileDisplayName,
                ["bio"] = Bio,
                ["favoriteTcg"] = "mtg",
                ["formatIds"] = StandardOnly,
            }, session);
            await Put(client, "/api/v1/me/avatar/preset", new() { ["presetId"] = "preset-01" }, session);
            _factory.Processor.RejectWith(AvatarRejectionReason.Content);
            await PostFile(client, "/api/v1/me/avatar/custom", [1, 2, 3, 4], session);

            await Post(client, "/api/v1/auth/logout", null, session);

            // redefinicao: pedido (existente e desconhecido), conclusao invalida e valida
            await Post(client, "/api/v1/auth/password-reset/request", new() { ["email"] = AnaEmail });
            await Post(client, "/api/v1/auth/password-reset/request", new() { ["email"] = "ninguem@example.com" });
            var link = (_factory.Notifier.Sent.Count > 0 ? _factory.Notifier.Sent[^1].Link : null);
            if (link is not null)
            {
                AddSecret("link de redefinicao", link);
                var token = link[(link.IndexOf("#token=", StringComparison.Ordinal) + "#token=".Length)..];
                AddSecret("token de redefinicao", token);
                AddHashes("token de redefinicao", token);
            }

            await Post(client, "/api/v1/auth/password-reset/confirm", new()
            {
                ["token"] = "token-invalido-sentinela",
                ["newPassword"] = NewPassword,
            });
            AddSecret("token de redefinicao invalido", "token-invalido-sentinela");
            if (link is not null)
            {
                var token = link[(link.IndexOf("#token=", StringComparison.Ordinal) + "#token=".Length)..];
                await Post(client, "/api/v1/auth/password-reset/confirm", new()
                {
                    ["token"] = token,
                    ["newPassword"] = NewPassword,
                });
                _currentPassword = NewPassword;
            }

            // atraso progressivo: varias falhas seguidas na mesma conta e IP
            for (var i = 0; i < 8; i++)
            {
                await Post(client, "/api/v1/auth/login", Login(AnaEmail, WrongPassword));
            }

            // falha no envio do email de redefinicao (evento 1008)
            _factory.Notifier.FailWith(new InvalidOperationException("falha simulada do notificador"));
            await Post(client, "/api/v1/auth/password-reset/request", new() { ["email"] = AnaEmail });
        }

        private async Task RunGoogleFlows(HttpClient client)
        {
            // sucesso (gui ja tem Google), cancelado, falha do provedor, state divergente
            _factory.Verifier.Returns(Identity("sub-gui-sentinela", GuiEmail));
            await GoogleCallback(client, "oauth-code-ok-1", null);
            await GoogleCallback(client, "oauth-code-cancel-2", "access_denied");
            _factory.Verifier.Fail();
            await GoogleCallback(client, "oauth-code-fail-3", null);
            _factory.Verifier.Returns(Identity("sub-gui-sentinela", GuiEmail));
            await GoogleCallback(client, "oauth-code-state-4", null, forceState: "state-adulterado-sentinela");
            AddSecret("state adulterado", "state-adulterado-sentinela");

            // conta nova: concluir cadastro
            _factory.Verifier.Returns(Identity("sub-novo-sentinela", NovoEmail));
            var concluir = await GoogleCallback(client, "oauth-code-novo-5", null);
            var pending = PendingCookieOf(concluir);
            if (pending is not null)
            {
                await Post(client, "/api/v1/auth/google/complete-signup", new()
                {
                    ["displayName"] = NovoDisplayName,
                    ["acceptTerms"] = true,
                    ["termsVersion"] = Terms,
                    ["adultDeclaration"] = true,
                }, pending);
            }

            // email ja cadastrado com senha: vincular (senha errada, depois correta)
            _factory.Verifier.Returns(Identity("sub-ana-sentinela", AnaEmail));
            var vincular = await GoogleCallback(client, "oauth-code-ana-6", null);
            pending = PendingCookieOf(vincular);
            if (pending is not null)
            {
                await Post(client, "/api/v1/auth/google/link", new() { ["password"] = WrongPassword }, pending);
                await Post(client, "/api/v1/auth/google/link", new() { ["password"] = _currentPassword }, pending);
            }
        }

        private async Task RunDeletionFlows(HttpClient client)
        {
            var login = await Post(client, "/api/v1/auth/login", Login(AnaEmail, _currentPassword));
            var session = SessionCookieOf(login) ?? $"{SessionCookie}={LogFactory.SeedSessionToken}";
            await Post(client, "/api/v1/me/deletion", new() { ["confirm"] = true, ["password"] = WrongPassword }, session);
            await Post(client, "/api/v1/me/deletion", new() { ["confirm"] = true, ["password"] = _currentPassword }, session);
        }

        private async Task RunDatabaseFailureFlow(HttpClient client)
        {
            _failureStart = _logs.Count;
            _factory.Players.FailWith(FailingSqlState);
            await Post(client, "/api/v1/auth/login", Login(AnaEmail, AnaPassword));
        }

        private async Task CollectStoredHashes()
        {
            foreach (var email in new[] { GuiEmail, NovoEmail })
            {
                var player = await _factory.Players.Inner.FindByEmail(Deckloom.Accounts.Domain.Email.Create(email).Value);
                if (player?.PasswordHash is { } hash)
                {
                    AddSecret($"hash da senha de {email}", hash.ToStoredString());
                }
            }

            AddSecret("hash da senha (formato do fake)", "v1$pbkdf2-sha256$");
            var sample = await new FakePasswordHasher(600000).HashAsync(Password.Create(AnaPassword).Value);
            AddSecret("hash da senha de ana", sample.ToStoredString());
        }

        private async Task<HttpResponseMessage> GoogleCallback(
            HttpClient client, string code, string? error, string? forceState = null)
        {
            using var start = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/google/start?intent=signin");
            AddIp(start);
            var started = await client.SendAsync(start);
            var state = HttpUtility.ParseQueryString(started.Headers.Location!.Query)["state"]!;
            var oauth = SetCookies(started).Single(c => c.StartsWith(OAuthCookie + "=", StringComparison.Ordinal))
                .Split(';')[0];
            Collect(started);
            AddSecret("state OAuth", state);
            AddSecret("code OAuth", code);
            AddSecret("nonce OAuth", HttpUtility.ParseQueryString(started.Headers.Location.Query)["nonce"]!);
            AddSecret("code_challenge OAuth", HttpUtility.ParseQueryString(started.Headers.Location.Query)["code_challenge"]!);

            var query = error is null
                ? $"code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(forceState ?? state)}"
                : $"error={error}&state={Uri.EscapeDataString(forceState ?? state)}";
            using var callback = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/auth/google/callback?{query}");
            callback.Headers.Add("Cookie", oauth);
            AddIp(callback);
            var response = await client.SendAsync(callback);
            Collect(response);
            return response;
        }

        private static GoogleVerifiedIdentity Identity(string subject, string email) =>
            new(subject, Deckloom.Accounts.Domain.Email.Create(email).Value, true, "Nome Google Sentinela", null);

        private static Dictionary<string, object?> Login(string email, string password) => new()
        {
            ["email"] = email,
            ["password"] = password,
            ["rememberMe"] = false,
        };

        private async Task<HttpResponseMessage> Post(
            HttpClient client, string path, Dictionary<string, object?>? body, string? cookie = null) =>
            await Send(client, HttpMethod.Post, path, JsonContent(body), cookie, withCsrf: true);

        private async Task<HttpResponseMessage> PostWithoutCsrf(
            HttpClient client, string path, Dictionary<string, object?> body) =>
            await Send(client, HttpMethod.Post, path, JsonContent(body), null, withCsrf: false);

        private async Task<HttpResponseMessage> Put(
            HttpClient client, string path, Dictionary<string, object?> body, string? cookie) =>
            await Send(client, HttpMethod.Put, path, JsonContent(body), cookie, withCsrf: true);

        private async Task<HttpResponseMessage> PostFile(HttpClient client, string path, byte[] bytes, string? cookie)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            var content = new MultipartFormDataContent { { file, "file", "avatar.png" } };
            return await Send(client, HttpMethod.Post, path, content, cookie, withCsrf: true);
        }

        private static StringContent? JsonContent(Dictionary<string, object?>? body) =>
            body is null ? null : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private async Task<HttpResponseMessage> Send(
            HttpClient client, HttpMethod method, string path, HttpContent? content, string? cookie, bool withCsrf)
        {
            using var request = new HttpRequestMessage(method, path) { Content = content };
            var cookies = new List<string>();
            if (cookie is not null)
            {
                cookies.Add(cookie);
            }

            request.Headers.Add("Origin", PublicOrigin);
            if (withCsrf)
            {
                using var csrf = new HttpRequestMessage(HttpMethod.Get, "/api/v1/csrf");
                AddIp(csrf);
                var csrfResponse = await client.SendAsync(csrf);
                var token = (await csrfResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
                AddSecret("token CSRF", token);
                cookies.Add(SetCookies(csrfResponse).Single(c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal))
                    .Split(';')[0]);
                request.Headers.Add("X-CSRF-Token", token);
                Collect(csrfResponse);
            }

            if (cookies.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", cookies));
            }

            AddIp(request);
            var response = await client.SendAsync(request);
            Collect(response);
            return response;
        }

        private static void AddIp(HttpRequestMessage request) => request.Headers.Add(IpHeader, ClientIp);

        private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
            response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];

        private static string? SessionCookieOf(HttpResponseMessage response) =>
            SetCookies(response).Where(c => c.StartsWith(SessionCookie + "=", StringComparison.Ordinal))
                .Select(c => c.Split(';')[0]).FirstOrDefault(c => c.Length > SessionCookie.Length + 1);

        private static string? PendingCookieOf(HttpResponseMessage response) =>
            SetCookies(response).Where(c => c.StartsWith(PendingCookie + "=", StringComparison.Ordinal))
                .Select(c => c.Split(';')[0]).FirstOrDefault(c => c.Length > PendingCookie.Length + 1);

        // Todo valor de cookie emitido pelo servidor (sessao, pendencia, OAuth, CSRF) e sensivel, assim como seus hashes.
        private void Collect(HttpResponseMessage response)
        {
            foreach (var setCookie in SetCookies(response))
            {
                var pair = setCookie.Split(';')[0];
                var index = pair.IndexOf('=', StringComparison.Ordinal);
                var name = pair[..index];
                var value = pair[(index + 1)..];
                if (value.Length < 4)
                {
                    continue;
                }

                AddSecret($"cookie {name}", value);
                AddSecret($"cookie {name} (decodificado)", Uri.UnescapeDataString(value));
                AddHashes($"cookie {name}", Uri.UnescapeDataString(value));
            }
        }

        private void AddSecret(string label, string value)
        {
            if (!string.IsNullOrEmpty(value) && !_secrets.Any(s => s.Value == value))
            {
                _secrets.Add((label, value));
            }
        }

        private void AddHashes(string label, string token)
        {
            AddBytes($"hash de {label}", SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        private void AddBytes(string label, byte[] bytes)
        {
            AddSecret($"{label} (hex)", Convert.ToHexString(bytes));
            AddSecret($"{label} (base64)", Convert.ToBase64String(bytes));
            AddSecret($"{label} (base64url)", Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        }
    }

    private sealed class ToggleablePlayerRepository(InMemoryPlayerRepository inner) : IPlayerRepository
    {
        private string? _sqlState;

        public InMemoryPlayerRepository Inner => inner;

        public void FailWith(string sqlState) => _sqlState = sqlState;

        public Task<Player?> GetById(PlayerId id, CancellationToken ct = default) => Guard(() => inner.GetById(id, ct));

        public Task<Player?> FindByEmail(Deckloom.Accounts.Domain.Email email, CancellationToken ct = default) =>
            Guard(() => inner.FindByEmail(email, ct));

        public Task<Player?> FindByGoogleSubject(string subject, CancellationToken ct = default) =>
            Guard(() => inner.FindByGoogleSubject(subject, ct));

        public Task<Player?> FindByResetTokenHash(TokenHash tokenHash, CancellationToken ct = default) =>
            Guard(() => inner.FindByResetTokenHash(tokenHash, ct));

        public Task<bool> ExistsByEmail(Deckloom.Accounts.Domain.Email email, CancellationToken ct = default) =>
            Guard(() => inner.ExistsByEmail(email, ct));

        public Task Add(Player player, CancellationToken ct = default) => Guard(() => inner.Add(player, ct));

        public Task Save(Player player, CancellationToken ct = default) => Guard(() => inner.Save(player, ct));

        public Task Remove(Player player, CancellationToken ct = default) => Guard(() => inner.Remove(player, ct));

        public Task<int> DeleteExpiredResetTokens(DateTimeOffset now, int batchSize, CancellationToken ct = default) =>
            Guard(() => inner.DeleteExpiredResetTokens(now, batchSize, ct));

        private Task<T> Guard<T>(Func<Task<T>> call) =>
            _sqlState is null ? call() : throw new FakeDbException(_sqlState);

        private Task Guard(Func<Task> call) =>
            _sqlState is null ? call() : throw new FakeDbException(_sqlState);
    }

    // A mensagem do servidor carrega SQL, parametro, host e connection string de proposito: nada disso pode ir a log.
    private sealed class FakeDbException(string sqlState) : DbException(
        "permission denied for table player (server detail): SELECT * FROM accounts.player WHERE email = @p0 " +
        "[host db-host-synthetic.internal; Username=deckloom_app;Password=test-db-secret-9f3a] ana@example.com senha-valida-123")
    {
        public override string? SqlState => sqlState;
    }

    private sealed class CapturingProvider(ConcurrentQueue<LoggedEntry> sink) : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, sink, () => _scopes);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(
        string category, ConcurrentQueue<LoggedEntry> sink, Func<IExternalScopeProvider> scopes) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => scopes().Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var props = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                {
                    props[pair.Key] = pair.Value;
                }
            }

            var captured = new List<string>();
            scopes().ForEachScope(
                (scope, list) =>
                {
                    list.Add(scope?.ToString() ?? string.Empty);
                    if (scope is IEnumerable<KeyValuePair<string, object?>> scopePairs)
                    {
                        list.AddRange(scopePairs.Select(p => $"{p.Key}={p.Value}"));
                    }
                },
                captured);

            sink.Enqueue(new LoggedEntry(
                category, eventId.Id, eventId.Name, logLevel, formatter(state, exception), props, captured, exception?.ToString()));
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class LogFactory(ConcurrentQueue<LoggedEntry> sink) : WebApplicationFactory<Program>
    {
        public const string SeedSessionToken = "gui-session-token-sentinela";
        private const string Terms = "2026-10";
        private const string IpHeader = "X-Test-Client-Ip";

        private readonly InMemoryUnitOfWork _uow = new();
        private readonly TimeProvider _clock = new FixedClock();

        public ToggleablePlayerRepository Players { get; private set; } = null!;

        public FakeGoogleIdentityVerifier Verifier { get; } = new();

        public RecordingPasswordResetNotifier Notifier { get; } = new();

        public FakeAvatarImageProcessor Processor { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.UseSetting("Auth:PublicOrigin", "https://deckloom.example.test");
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(new CapturingProvider(sink));
            });
            var inner = new InMemoryPlayerRepository(_uow);
            Players = new ToggleablePlayerRepository(inner);
            var sessions = new InMemorySessionRepository(_uow);
            Seed(inner, sessions).GetAwaiter().GetResult();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.RemoveAll<IUnitOfWork>();
                services.RemoveDatabaseRoleCheck();
                services.RemoveAll<IPlayerRepository>();
                services.RemoveAll<ISessionRepository>();
                services.RemoveAll<IAccessAttemptStore>();
                services.RemoveAll<IPendingGoogleAuthStore>();
                services.RemoveAll<IGoogleIdentityVerifier>();
                services.RemoveAll<IPlayerDecksPurger>();
                services.RemoveAll<IAvatarStorage>();
                services.RemoveAll<IAvatarImageProcessor>();
                services.RemoveAll<IPasswordResetNotifier>();
                services.RemoveAll<IPasswordHasher>();
                services.RemoveAll<IKeyedHasher>();
                services.RemoveAll<IDelayer>();
                services.RemoveAll<ITokenHasher>();
                services.RemoveAll<ISecureTokenGenerator>();
                services.RemoveAll<IFormatCatalog>();
                services.RemoveAll<IAvatarPresetCatalog>();
                services.RemoveAll<ITermsVersionProvider>();
                services.AddSingleton(_clock);
                services.AddSingleton<IUnitOfWork>(_uow);
                services.AddSingleton<IPlayerRepository>(Players);
                services.AddSingleton<ISessionRepository>(sessions);
                services.AddSingleton<IAccessAttemptStore>(new InMemoryAccessAttemptStore(_uow));
                services.AddSingleton<IPendingGoogleAuthStore>(new InMemoryPendingGoogleAuthStore(_clock, _uow));
                services.AddSingleton<IGoogleIdentityVerifier>(Verifier);
                services.AddSingleton<IPlayerDecksPurger>(new RecordingPlayerDecksPurger(_uow));
                services.AddSingleton<IAvatarStorage>(new InMemoryAvatarStorage(_uow));
                services.AddSingleton<IAvatarImageProcessor>(Processor);
                services.AddSingleton<IPasswordResetNotifier>(Notifier);
                services.AddSingleton<IPasswordHasher>(new FakePasswordHasher(600000));
                services.AddSingleton<IKeyedHasher>(new FakeKeyedHasher());
                services.AddSingleton<IDelayer>(new RecordingDelayer());
                services.AddSingleton<ITokenHasher>(new Sha256TokenHasher());
                services.AddSingleton<ISecureTokenGenerator>(new SequentialTokenGenerator());
                services.AddSingleton<IFormatCatalog>(new InMemoryFormatCatalog(
                    [new FormatEntry(new FormatId("standard"), "Standard", 10, true)]));
                services.AddSingleton<IAvatarPresetCatalog>(new InMemoryAvatarPresetCatalog(
                    [new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, true, true)]));
                services.AddSingleton<ITermsVersionProvider>(new FixedTermsVersionProvider(
                    Terms,
                    new Uri("https://deckloom.example.test/termos"),
                    new Uri("https://deckloom.example.test/privacidade")));
                services.AddSingleton<IStartupFilter>(new ClientIpFilter());
            });
        }

        private async Task Seed(InMemoryPlayerRepository players, InMemorySessionRepository sessions)
        {
            var now = _clock.GetUtcNow();
            var gui = Player.RegisterWithGoogle(
                Deckloom.Accounts.Domain.Email.Create("gui@example.com").Value,
                ExternalIdentity.Create("google", "sub-gui-sentinela").Value,
                DisplayName.Create("Gui").Value,
                Terms,
                Terms,
                true,
                true,
                now,
                "preset-01").Value;
            await players.Add(gui);
            await sessions.Add(Session.Start(gui.Id, new Sha256TokenHasher().Hash(SeedSessionToken), false, now));
            await _uow.CommitAsync();
        }

        private sealed class ClientIpFilter : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
                app =>
                {
                    app.Use(async (context, nextMiddleware) =>
                    {
                        if (context.Request.Headers.TryGetValue(IpHeader, out var ip) && IPAddress.TryParse(ip, out var address))
                        {
                            context.Connection.RemoteIpAddress = address;
                        }

                        await nextMiddleware();
                    });
                    next(app);
                };
        }
    }
}
