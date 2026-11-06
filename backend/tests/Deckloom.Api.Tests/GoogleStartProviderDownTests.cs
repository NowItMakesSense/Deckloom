using System.Net;
using Deckloom.Accounts.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Deckloom.Api.Tests;

// QA-06 / R-33 (RED registrado, Skip ate o implementer corrigir): com o Google fora do ar, o inicio do login
// (navegacao de topo) deve terminar em 302 para o retorno com resultado=falhou (design.md:1575, 1411), nunca em 500 JSON.
// Medido em 2026-10-31: devolve 500 InternalServerError. Remover o Skip ao corrigir o endpoint.
public sealed class GoogleStartProviderDownTests
{
    private sealed class Throwing : IGoogleIdentityVerifier
    {
        public Task<Uri> BuildAuthorizationUrlAsync(string state, string nonce, string codeChallenge, GoogleAuthIntent intent = GoogleAuthIntent.SignIn, CancellationToken ct = default) =>
            throw new HttpRequestException("google fora do ar");
        public Task<GoogleVerifiedIdentity?> CompleteAsync(string code, string codeVerifier, string nonce, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task R33_start_with_google_down_redirects_to_the_return_page_with_falhou()
    {
        using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSyntheticModuleSettings();
            b.UseSetting("Auth:PublicOrigin", "https://deckloom.example.test");
            b.ConfigureServices(s =>
            {
                s.RemoveDatabaseRoleCheck();
                s.RemoveAll<IGoogleIdentityVerifier>();
                s.AddSingleton<IGoogleIdentityVerifier>(new Throwing());
            });
        });
        using var c = f.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var r = await c.GetAsync("/api/v1/auth/google/start?intent=signin");
        Assert.Equal(HttpStatusCode.Found, r.StatusCode);
        Assert.Equal("/entrar/google/retorno?resultado=falhou", r.Headers.Location!.OriginalString);
    }
}
