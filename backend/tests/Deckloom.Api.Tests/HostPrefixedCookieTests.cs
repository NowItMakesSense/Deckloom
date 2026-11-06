using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Deckloom.Api.Tests;

// T-147 (retro item 3; guarda de SEC-18 e SEC-04): na composicao real (adapters EF em SQLite, http puro, Development),
// todo Set-Cookie com prefixo __Host- tem Secure, Path=/ e nenhum Domain, inclusive os que apagam o cookie. O teste exige
// que cada um dos quatro cookies tenha sido observado, para nao passar vazio. Uma mutacao real (cadastro) feita com o
// token CSRF obtido do cookie nao pode voltar 403 csrf_failed.
public sealed class HostPrefixedCookieTests : IDisposable
{
    private const string CsrfCookie = "__Host-dl_csrf";
    private const string SessionCookie = "__Host-dl_session";
    private const string OAuthCookie = "__Host-dl_oauth";
    private const string PendingCookie = "__Host-dl_pending";
    private const string Terms = "2026-10";
    private const string Password = "senha-valida-123";

    private readonly RealCompositionWebApplicationFactory _factory = new();
    private readonly HashSet<string> _observed = new(StringComparer.Ordinal);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task The_real_composition_does_not_treat_requests_as_https()
    {
        using var client = NewClient();

        var scheme = await client.GetStringAsync(RealCompositionWebApplicationFactory.SchemeProbePath);

        Assert.Equal("http", scheme);
    }

    [Fact]
    public async Task SEC_18_every_host_prefixed_Set_Cookie_has_Secure_Path_root_and_no_Domain_and_all_four_cookies_are_observed()
    {
        using var client = NewClient();

        // CSRF: GET /api/v1/csrf emite __Host-dl_csrf.
        var csrf = await client.GetAsync("/api/v1/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        Observe(csrf);
        var csrfToken = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var csrfPair = CookiePair(csrf, CsrfCookie);

        // Cadastro: mutacao com CSRF, nao pode ser 403 csrf_failed; emite __Host-dl_session.
        var register = await Send(client, HttpMethod.Post, "/api/v1/auth/register", csrfToken, csrfPair, Json(new
        {
            email = "ana@example.com",
            password = Password,
            displayName = "Ana",
            acceptTerms = true,
            termsVersion = Terms,
            adultDeclaration = true,
        }));
        Assert.True(
            (int)register.StatusCode is >= 200 and < 300,
            $"Cadastro devolveu {(int)register.StatusCode} em vez de 2xx.");
        Observe(register);
        var sessionPair = CookiePair(register, SessionCookie);

        // Logout: apaga o cookie de sessao.
        var logout = await Send(client, HttpMethod.Post, "/api/v1/auth/logout", csrfToken, $"{csrfPair}; {sessionPair}", null);
        Observe(logout);

        // Login com e sem "lembrar-me".
        foreach (var rememberMe in new[] { false, true })
        {
            var login = await Send(client, HttpMethod.Post, "/api/v1/auth/login", csrfToken, csrfPair, Json(new
            {
                email = "ana@example.com",
                password = Password,
                rememberMe,
            }));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Observe(login);
        }

        // Google: start emite __Host-dl_oauth; callback com email novo apaga o oauth e emite __Host-dl_pending.
        var start = await client.GetAsync("/api/v1/auth/google/start");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Observe(start);
        var oauthPair = CookiePair(start, OAuthCookie);
        var state = QueryValue(start.Headers.Location!, "state");
        _factory.Google.Returns(new GoogleVerifiedIdentity(
            "google-sub-novo", Email.Create("novo@example.com").Value, true, "Nova Pessoa"));

        var callback = await Send(
            client,
            HttpMethod.Get,
            $"/api/v1/auth/google/callback?code=codigo-1&state={Uri.EscapeDataString(state)}",
            null,
            oauthPair,
            null);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Observe(callback);
        var pendingPair = CookiePair(callback, PendingCookie);

        // Cancelar a pendencia apaga o cookie __Host-dl_pending.
        var cancel = await Send(client, HttpMethod.Delete, "/api/v1/auth/google/pending", csrfToken, $"{csrfPair}; {pendingPair}", null);
        Observe(cancel);

        foreach (var name in new[] { CsrfCookie, SessionCookie, OAuthCookie, PendingCookie })
        {
            Assert.Contains(name, _observed);
        }
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

    private void Observe(HttpResponseMessage response)
    {
        foreach (var name in HostCookieAssertions.AssertAllHostPrefixed(response))
        {
            _observed.Add(name);
        }
    }

    private static string Json(object body) => JsonSerializer.Serialize(body);

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string? csrfToken, string cookies, string? jsonBody)
    {
        using var request = new HttpRequestMessage(method, path);
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        request.Headers.Add("Cookie", cookies);
        if (csrfToken is not null)
        {
            request.Headers.Add("Origin", RealCompositionWebApplicationFactory.PublicOrigin);
            request.Headers.Add("X-CSRF-Token", csrfToken);
        }

        return await client.SendAsync(request);
    }

    // "nome=valor" do Set-Cookie vivo (nao o que expira) com esse nome, pronto para o cabecalho Cookie.
    private static string CookiePair(HttpResponseMessage response, string name) =>
        Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith(name + "=", StringComparison.Ordinal)
                && !c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
            .Split(';')[0];

    private static string QueryValue(Uri uri, string key)
    {
        var pair = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).First(p => p[0] == key);
        return Uri.UnescapeDataString(pair[1]);
    }
}
