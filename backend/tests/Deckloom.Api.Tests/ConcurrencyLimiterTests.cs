using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Deckloom.Api.Tests;

// T-091 (RED, REQ-032, ADR-0005 item 5): limitador de concorrencia por IP nos endpoints de autenticacao
// (/api/v1/auth/**): no maximo 4 simultaneas, fila de 16, alem disso 503 generico sem corpo informativo
// (nao e bloqueio de conta); corpo JSON > 16 KB e upload multipart > 2,5 MB recusados (413); limites configuraveis.
//
// Lacunas assumidas (ver relatorio): (1) chaves de configuracao
//   Http:ConcurrencyLimiter:PermitLimit (4), Http:ConcurrencyLimiter:QueueLimit (16),
//   Http:Limits:MaxJsonBodyBytes (16384), Http:Limits:MaxUploadBytes (2621440);
// (2) o limitador usa HttpContext.Connection.RemoteIpAddress; X-Forwarded-For so vale vindo de proxy confiavel (loopback ou Forwarding:TrustedProxies, R-14);
// (3) os limites de corpo valem em middleware, por Content-Length, para JSON em /api/** e multipart em /api/**,
//     antes do CSRF e da leitura do corpo; status 413; (4) 503 "generico" = corpo vazio, sem Problem Details.
public sealed class ConcurrencyLimiterTests : IDisposable
{
    private const string PublicOrigin = "https://deckloom.example.test";
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string IpHeader = "X-Test-Ip";
    private const string BlockPath = "/api/v1/auth/__test/block";
    private const string FastPath = "/api/v1/auth/__test/fast";
    private const string OutsideBlockPath = "/api/v1/catalog/__test/block";
    private const string JsonEchoPath = "/api/v1/auth/__test/echo";
    private const string UploadEchoPath = "/api/v1/me/__test/upload";

    private readonly List<LimiterFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Gate.Release();
            factory.Dispose();
        }
    }

    [Fact]
    public async Task REQ032_up_to_four_simultaneous_requests_per_ip_are_served_and_queue_holds_sixteen()
    {
        var factory = NewFactory();
        using var client = NewClient(factory);

        var inFlight = Enumerable.Range(0, 20).Select(_ => Block(client, "10.0.0.1")).ToList();
        await factory.Gate.WaitUntilEntered(4);

        // Fila: as outras 16 aguardam; so 4 chegam ao handler ao mesmo tempo.
        await Task.Delay(300);
        Assert.Equal(4, factory.Gate.Entered);
        Assert.All(inFlight, t => Assert.False(t.IsCompleted));

        factory.Gate.Release();
        var responses = await Task.WhenAll(inFlight);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(20, factory.Gate.Entered);
        Assert.True(factory.Gate.MaxConcurrent <= 4);
    }

    [Fact]
    public async Task REQ032_request_beyond_the_queue_gets_generic_503_without_informative_body()
    {
        var factory = NewFactory();
        using var client = NewClient(factory);
        var inFlight = Enumerable.Range(0, 20).Select(_ => Block(client, "10.0.0.2")).ToList();
        await factory.Gate.WaitUntilEntered(4);
        await Task.Delay(300);

        var rejected = await Rejected(client, "10.0.0.2");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        Assert.Equal(string.Empty, await rejected.Content.ReadAsStringAsync());
        Assert.False(rejected.Headers.Contains("Set-Cookie"));
        factory.Gate.Release();
        await Task.WhenAll(inFlight);
    }

    [Fact]
    public async Task REQ032_503_is_not_an_account_lock_and_ip_is_served_again_when_slots_free()
    {
        var factory = NewFactory();
        using var client = NewClient(factory);
        var inFlight = Enumerable.Range(0, 20).Select(_ => Block(client, "10.0.0.3")).ToList();
        await factory.Gate.WaitUntilEntered(4);
        await Task.Delay(300);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Rejected(client, "10.0.0.3")).StatusCode);

        factory.Gate.Release();
        await Task.WhenAll(inFlight);
        var after = await client.SendAsync(Get(FastPath, "10.0.0.3"));

        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task REQ032_other_ips_are_not_affected_by_a_saturated_ip()
    {
        var factory = NewFactory();
        using var client = NewClient(factory);
        var inFlight = Enumerable.Range(0, 20).Select(_ => Block(client, "10.0.0.4")).ToList();
        await factory.Gate.WaitUntilEntered(4);
        await Task.Delay(300);

        var saturated = await client.SendAsync(Get(FastPath, "10.0.0.4"));
        var other = await client.SendAsync(Get(FastPath, "10.0.0.5"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, saturated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        factory.Gate.Release();
        await Task.WhenAll(inFlight);
    }

    [Fact]
    public async Task REQ032_endpoints_outside_authentication_are_not_limited()
    {
        var factory = NewFactory();
        using var client = NewClient(factory);
        var inFlight = Enumerable.Range(0, 25)
            .Select(_ => client.SendAsync(Get(OutsideBlockPath, "10.0.0.6"))).ToList();

        await factory.Gate.WaitUntilEntered(25);

        factory.Gate.Release();
        var responses = await Task.WhenAll(inFlight);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task REQ032_limits_are_configurable()
    {
        var factory = NewFactory(("Http:ConcurrencyLimiter:PermitLimit", "1"), ("Http:ConcurrencyLimiter:QueueLimit", "1"));
        using var client = NewClient(factory);
        var first = Block(client, "10.0.0.7");
        await factory.Gate.WaitUntilEntered(1);
        var queued = Block(client, "10.0.0.7");
        await Task.Delay(300);

        var rejected = await Rejected(client, "10.0.0.7");

        Assert.Equal(1, factory.Gate.Entered);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        factory.Gate.Release();
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await queued).StatusCode);
    }

    [Fact]
    public async Task REQ032_behind_a_loopback_proxy_the_limiter_keys_on_the_forwarded_client_ip()
    {
        var factory = NewFactory();
        using var client = NewClient(factory);
        var inFlight = Enumerable.Range(0, 20).Select(_ => Block(client, "127.0.0.1", "203.0.113.1")).ToList();
        await factory.Gate.WaitUntilEntered(4);
        await Task.Delay(300);

        // Outro cliente atras do mesmo proxy tem a propria cota: entra no handler em vez de levar 503.
        var other = Block(client, "127.0.0.1", "203.0.113.2");
        await factory.Gate.WaitUntilEntered(5);
        var sameClient = await Rejected(client, "127.0.0.1", "203.0.113.1");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, sameClient.StatusCode);
        factory.Gate.Release();
        Assert.Equal(HttpStatusCode.OK, (await other).StatusCode);
        await Task.WhenAll(inFlight);
    }

    [Fact]
    public async Task REQ032_behind_a_configured_proxy_range_the_limiter_keys_on_the_forwarded_client_ip()
    {
        var factory = NewFactory(("Forwarding:TrustedProxies:0", "172.28.10.0/24"));
        using var client = NewClient(factory);
        var inFlight = Enumerable.Range(0, 20).Select(_ => Block(client, "172.28.10.2", "203.0.113.1")).ToList();
        await factory.Gate.WaitUntilEntered(4);
        await Task.Delay(300);

        var other = Block(client, "172.28.10.2", "203.0.113.2");
        await factory.Gate.WaitUntilEntered(5);
        var sameClient = await Rejected(client, "172.28.10.2", "203.0.113.1");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, sameClient.StatusCode);
        factory.Gate.Release();
        Assert.Equal(HttpStatusCode.OK, (await other).StatusCode);
        await Task.WhenAll(inFlight);
    }

    [Fact]
    public async Task SEC02_addresses_in_the_same_ipv6_slash_64_share_the_limiter_partition_and_other_prefixes_do_not()
    {
        var factory = NewFactory();
        using var client = NewClient(factory);
        var inFlight = Enumerable.Range(0, 20).Select(_ => Block(client, "2001:db8:1:2::1")).ToList();
        await factory.Gate.WaitUntilEntered(4);
        await Task.Delay(300);

        var sameSlash64 = await Rejected(client, "2001:db8:1:2:ffff:ffff:ffff:ffff");
        var otherSlash64 = Block(client, "2001:db8:1:3::1");
        await factory.Gate.WaitUntilEntered(5);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, sameSlash64.StatusCode);
        factory.Gate.Release();
        Assert.Equal(HttpStatusCode.OK, (await otherSlash64).StatusCode);
        await Task.WhenAll(inFlight);
    }

    [Theory]
    [InlineData("172.28.11.2")]
    [InlineData("127.0.0.1")]
    public async Task REQ032_forwarded_for_from_outside_the_configured_proxy_range_is_ignored(string remoteIp)
    {
        var factory = NewFactory(("Forwarding:TrustedProxies:0", "172.28.10.0/24"));
        using var client = NewClient(factory);
        var inFlight = Enumerable.Range(0, 20).Select(_ => Block(client, remoteIp, "203.0.113.1")).ToList();
        await factory.Gate.WaitUntilEntered(4);
        await Task.Delay(300);

        var spoofed = await Rejected(client, remoteIp, "203.0.113.99");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, spoofed.StatusCode);
        Assert.Equal(4, factory.Gate.Entered);
        factory.Gate.Release();
        await Task.WhenAll(inFlight);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("172.28.10.0/33")]
    [InlineData("")]
    public void REQ032_invalid_trusted_proxy_entry_fails_at_startup_with_a_clear_message(string entry)
    {
        var factory = NewFactory(("Forwarding:TrustedProxies:0", entry));

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Forwarding:TrustedProxies", Flatten(error));
    }

    private static string Flatten(Exception error) =>
        string.Join(" | ", Chain(error).Select(e => e.Message));

    private static IEnumerable<Exception> Chain(Exception? error)
    {
        for (; error is not null; error = error.InnerException)
        {
            yield return error;
        }
    }

    [Theory]
    [InlineData("10.0.0.9")]
    [InlineData("127.0.0.2")]
    public async Task REQ032_forwarded_for_from_a_non_trusted_origin_is_ignored(string remoteIp)
    {
        var factory = NewFactory();
        using var client = NewClient(factory);
        var inFlight = Enumerable.Range(0, 20).Select(_ => Block(client, remoteIp, "203.0.113.1")).ToList();
        await factory.Gate.WaitUntilEntered(4);
        await Task.Delay(300);

        // Mesmo mudando o X-Forwarded-For, a origem nao confiavel segue sendo uma unica chave: 503.
        var spoofed = await Rejected(client, remoteIp, "203.0.113.99");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, spoofed.StatusCode);
        Assert.Equal(4, factory.Gate.Entered);
        factory.Gate.Release();
        await Task.WhenAll(inFlight);
    }

    [Fact]
    public async Task REQ032_json_body_above_16_kb_is_refused_before_reaching_the_handler()
    {
        var factory = NewFactory();
        using var client = NewClient(factory);

        var response = await PostJson(client, JsonEchoPath, new string('a', 16 * 1024 + 1));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, factory.Gate.Echoed);
    }

    [Fact]
    public async Task REQ032_json_body_within_16_kb_is_accepted()
    {
        var factory = NewFactory();
        using var client = NewClient(factory);

        var response = await PostJson(client, JsonEchoPath, new string('a', 8 * 1024));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, factory.Gate.Echoed);
    }

    [Fact]
    public async Task REQ032_json_body_limit_is_configurable()
    {
        var factory = NewFactory(("Http:Limits:MaxJsonBodyBytes", "1024"));
        using var client = NewClient(factory);

        var response = await PostJson(client, JsonEchoPath, new string('a', 2 * 1024));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, factory.Gate.Echoed);
    }

    [Fact]
    public async Task REQ032_upload_above_2_5_mb_is_refused_before_reaching_the_handler()
    {
        var factory = NewFactory();
        using var client = NewClient(factory);

        var response = await PostMultipart(client, UploadEchoPath, 2_621_440 + 1);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, factory.Gate.Echoed);
    }

    [Fact]
    public async Task REQ032_upload_within_2_5_mb_is_accepted()
    {
        var factory = NewFactory();
        using var client = NewClient(factory);

        var response = await PostMultipart(client, UploadEchoPath, 2_000_000);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, factory.Gate.Echoed);
    }

    [Fact]
    public async Task REQ032_upload_limit_is_configurable()
    {
        var factory = NewFactory(("Http:Limits:MaxUploadBytes", "100000"));
        using var client = NewClient(factory);

        var response = await PostMultipart(client, UploadEchoPath, 200_000);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, factory.Gate.Echoed);
    }

    private LimiterFactory NewFactory(params (string Key, string Value)[] settings)
    {
        var factory = new LimiterFactory(settings);
        _factories.Add(factory);
        return factory;
    }

    private static HttpClient NewClient(LimiterFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });

    private static HttpRequestMessage Get(string path, string ip, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(IpHeader, ip);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return request;
    }

    private static Task<HttpResponseMessage> Block(HttpClient client, string ip, string? forwardedFor = null) =>
        Task.Run(() => client.SendAsync(Get(BlockPath, ip, forwardedFor)));

    // Requisicao que deve ser recusada de imediato; sem limitador ela ficaria presa no handler, entao falha por tempo.
    private static async Task<HttpResponseMessage> Rejected(HttpClient client, string ip, string? forwardedFor = null)
    {
        var pending = Block(client, ip, forwardedFor);
        var done = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.True(done == pending, "A requisicao acima da fila deveria ser recusada de imediato (503).");
        return await pending;
    }

    private static async Task<HttpResponseMessage> PostJson(HttpClient client, string path, string padding)
    {
        var (token, cookie) = await Csrf(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { padding }), Encoding.UTF8, "application/json"),
        };
        Decorate(request, token, cookie);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostMultipart(HttpClient client, string path, int fileBytes)
    {
        var (token, cookie) = await Csrf(client);
        var content = new MultipartFormDataContent { { new ByteArrayContent(new byte[fileBytes]), "file", "a.png" } };
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        Decorate(request, token, cookie);
        return await client.SendAsync(request);
    }

    private static void Decorate(HttpRequestMessage request, string token, string cookie)
    {
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add("X-CSRF-Token", token);
        request.Headers.Add(IpHeader, "10.9.9.9");
    }

    private static async Task<(string Token, string Cookie)> Csrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(CsrfCookie + "=", StringComparison.Ordinal));
        return (json.GetProperty("token").GetString() ?? string.Empty, cookie.Split(';')[0]);
    }

    private sealed class Gate
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;
        private int _current;
        private int _max;
        private int _echoed;

        public int Entered => Volatile.Read(ref _entered);

        public int MaxConcurrent => Volatile.Read(ref _max);

        public int Echoed => Volatile.Read(ref _echoed);

        public void Release() => _open.TrySetResult();

        public void Echo() => Interlocked.Increment(ref _echoed);

        public async Task Pass()
        {
            Interlocked.Increment(ref _entered);
            var now = Interlocked.Increment(ref _current);
            int seen;
            while (now > (seen = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, now, seen) != seen)
            {
            }

            try
            {
                await _open.Task;
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }

        public async Task WaitUntilEntered(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Entered < count)
            {
                Assert.True(DateTime.UtcNow < deadline, $"Apenas {Entered} de {count} requisicoes chegaram ao handler.");
                await Task.Delay(10);
            }
        }
    }

    private sealed class LimiterFactory(IEnumerable<(string Key, string Value)> settings) : WebApplicationFactory<Program>
    {
        public Gate Gate { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.UseSetting("Auth:PublicOrigin", PublicOrigin);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveDatabaseRoleCheck();
                services.AddSingleton<IStartupFilter>(new TestFilter(Gate));
            });
        }
    }

    private sealed class TestFilter(Gate gate) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                // Antes do pipeline da API: define o IP remoto do cliente de teste.
                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Headers.TryGetValue(IpHeader, out var ip) && IPAddress.TryParse(ip, out var address))
                    {
                        context.Connection.RemoteIpAddress = address;
                    }

                    await nextMiddleware();
                });
                next(app);
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet(BlockPath, async () =>
                    {
                        await gate.Pass();
                        return Results.Ok();
                    });
                    endpoints.MapGet(OutsideBlockPath, async () =>
                    {
                        await gate.Pass();
                        return Results.Ok();
                    });
                    endpoints.MapGet(FastPath, () => Results.Ok());
                    endpoints.MapPost(JsonEchoPath, () =>
                    {
                        gate.Echo();
                        return Results.Ok();
                    });
                    endpoints.MapPost(UploadEchoPath, () =>
                    {
                        gate.Echo();
                        return Results.Ok();
                    });
                });
            };
    }
}
