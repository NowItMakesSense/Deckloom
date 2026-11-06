using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deckloom.Accounts.Testing;

// Servidor OIDC de mentira em loopback (discovery, JWKS e token endpoint) com chave RSA de teste.
// Nenhum teste fala com o Google real.
public sealed class StubGoogleOidcServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly RSA _key = RSA.Create(2048);
    private readonly CancellationTokenSource _stop = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _tokenBodies = new();
    private readonly HashSet<string> _usedCodes = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AuthorizationGrant> _grants = new();
    private readonly object _identityLock = new();
    private BrowserIdentity _identity = new("stub-subject", "stub@example.test", null, true, true);
    private int _jwksRequests;
    private int _codeSequence;
    private Task _loop = Task.CompletedTask;

    private StubGoogleOidcServer(int port)
    {
        Authority = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(Authority.AbsoluteUri);
    }

    public Uri Authority { get; }

    public Uri AuthorizationEndpoint => new(Authority, "authorize");

    public string ClientId { get; } = "stub-client-id.apps.stub.test";

    public string ClientSecret { get; } = "stub-client-secret-never-real";

    public string KeyId { get; } = "stub-key-1";

    // Claims opcionais do id_token (T-068): so entram no token quando definidos.
    public string? Nonce { get; set; }

    public string? Name { get; set; }

    public string? Picture { get; set; }

    public DateTimeOffset? AuthTime { get; set; }

    // Relogio do host E2E (T-126): quando definido, iat/exp e o auth_time do fluxo de navegador seguem esse relogio
    // (o mesmo que o endpoint de teste adianta), nao o relogio real.
    public TimeProvider? Clock { get; set; }

    // Knobs de falha (T-069); todos neutros por padrao.
    public string? IssuerOverride { get; set; }

    public string? AudienceOverride { get; set; }

    public TimeSpan ExpiresIn { get; set; } = TimeSpan.FromHours(1);

    public bool EmailVerified { get; set; } = true;

    public bool CorruptSignature { get; set; }

    public int? TokenStatus { get; set; }

    public string? TokenError { get; set; }

    public TimeSpan TokenDelay { get; set; }

    public int JwksFailuresBeforeSuccess { get; set; }

    public bool EnforceSingleUseCodes { get; set; }

    // Identidade para o fluxo de navegador (E2E): vale para as proximas autorizacoes ate nova chamada.
    public void SetBrowserIdentity(string subject, string email, string? name, bool emailVerified, bool approve)
    {
        lock (_identityLock)
        {
            _identity = new BrowserIdentity(subject, email, name, emailVerified, approve);
        }
    }

    public int JwksRequestCount => Volatile.Read(ref _jwksRequests);

    // Corpos (form-urlencoded) recebidos no token endpoint, para afirmar o que o cliente envia.
    public IReadOnlyList<string> TokenRequestBodies => _tokenBodies.ToArray();

    public static StubGoogleOidcServer Start()
    {
        for (var attempt = 0; ; attempt++)
        {
            var server = new StubGoogleOidcServer(FreeLoopbackPort());
            try
            {
                server._listener.Start();
                server._loop = Task.Run(server.ServeAsync);
                return server;
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                server._listener.Close();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Close();
        await _loop;
        _key.Dispose();
        _stop.Dispose();
    }

    private static int FreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            Respond(context);
        }
    }

    private void Respond(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var body = string.Empty;
        if (path == "/token")
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            body = reader.ReadToEnd();
            _tokenBodies.Enqueue(body);
            if (TokenDelay > TimeSpan.Zero && _stop.Token.WaitHandle.WaitOne(TokenDelay))
            {
                context.Response.Abort();
                return;
            }
        }

        if (path == "/authorize")
        {
            Authorize(context);
            return;
        }

        var (status, json) = path switch
        {
            "/.well-known/openid-configuration" => (200, Discovery()),
            "/jwks" => JwksResponse(),
            "/token" => TokenResponse(body),
            _ => (404, "{}"),
        };

        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes);
        context.Response.Close();
    }

    private void Authorize(HttpListenerContext context)
    {
        var query = context.Request.QueryString;
        var redirectUri = query["redirect_uri"];
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out _))
        {
            context.Response.StatusCode = 400;
            context.Response.Close();
            return;
        }

        BrowserIdentity identity;
        lock (_identityLock)
        {
            identity = _identity;
        }

        var state = Uri.EscapeDataString(query["state"] ?? string.Empty);
        var separator = redirectUri.Contains('?') ? '&' : '?';
        string location;
        if (identity.Approve)
        {
            var code = $"stub-code-{Interlocked.Increment(ref _codeSequence)}-{Guid.NewGuid():N}";
            _grants[code] = new AuthorizationGrant(query["nonce"], identity);
            location = $"{redirectUri}{separator}code={code}&state={state}";
        }
        else
        {
            location = $"{redirectUri}{separator}error=access_denied&state={state}";
        }

        context.Response.StatusCode = 302;
        context.Response.RedirectLocation = location;
        context.Response.ContentLength64 = 0;
        context.Response.Close();
    }

    private (int Status, string Json) JwksResponse()
    {
        var count = Interlocked.Increment(ref _jwksRequests);
        return count <= JwksFailuresBeforeSuccess ? (503, "{}") : (200, Jwks());
    }

    private (int Status, string Json) TokenResponse(string body)
    {
        if (TokenStatus is { } status)
        {
            return (status, "{}");
        }

        if (TokenError is not null)
        {
            return (400, JsonSerializer.Serialize(new { error = TokenError }));
        }

        if (EnforceSingleUseCodes)
        {
            var code = System.Web.HttpUtility.ParseQueryString(body)["code"] ?? string.Empty;
            bool fresh;
            lock (_usedCodes)
            {
                fresh = _usedCodes.Add(code);
            }

            if (!fresh)
            {
                return (400, JsonSerializer.Serialize(new { error = "invalid_grant" }));
            }
        }

        var tokenCode = System.Web.HttpUtility.ParseQueryString(body)["code"] ?? string.Empty;
        return (200, Token(_grants.TryGetValue(tokenCode, out var grant) ? grant : null));
    }

    private string Discovery() => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["issuer"] = Authority.AbsoluteUri.TrimEnd('/'),
        ["authorization_endpoint"] = AuthorizationEndpoint.AbsoluteUri,
        ["token_endpoint"] = new Uri(Authority, "token").AbsoluteUri,
        ["jwks_uri"] = new Uri(Authority, "jwks").AbsoluteUri,
    });

    private string Jwks()
    {
        var p = _key.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = "RSA", use = "sig", alg = "RS256", kid = KeyId, n = B64(p.Modulus!), e = B64(p.Exponent!) },
            },
        });
    }

    private string Token(AuthorizationGrant? grant = null)
    {
        var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = KeyId }));
        var now = (Clock?.GetUtcNow() ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var claims = new Dictionary<string, object>
        {
            ["iss"] = IssuerOverride ?? Authority.AbsoluteUri.TrimEnd('/'),
            ["aud"] = AudienceOverride ?? ClientId,
            ["sub"] = grant?.Identity.Subject ?? "stub-subject",
            ["email"] = grant?.Identity.Email ?? "stub@example.test",
            ["email_verified"] = grant?.Identity.EmailVerified ?? EmailVerified,
            ["iat"] = now,
            ["exp"] = now + (long)ExpiresIn.TotalSeconds,
        };
        var nonce = grant is null ? Nonce : grant.Nonce;
        if (nonce is not null)
        {
            claims["nonce"] = nonce;
        }

        var name = grant is null ? Name : grant.Identity.Name;
        if (name is not null)
        {
            claims["name"] = name;
        }

        if (Picture is not null)
        {
            claims["picture"] = Picture;
        }

        if (AuthTime is not null)
        {
            claims["auth_time"] = AuthTime.Value.ToUnixTimeSeconds();
        }
        else if (grant is not null && Clock is not null)
        {
            claims["auth_time"] = now;
        }

        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(claims));
        var signature = _key.SignData(
            Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (CorruptSignature)
        {
            signature[0] ^= 0xFF;
        }

        return JsonSerializer.Serialize(new
        {
            access_token = "stub-access-token",
            token_type = "Bearer",
            id_token = $"{header}.{payload}.{B64(signature)}",
        });
    }

    private static string B64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record BrowserIdentity(string Subject, string Email, string? Name, bool EmailVerified, bool Approve);

    private sealed record AuthorizationGrant(string? Nonce, BrowserIdentity Identity);
}
