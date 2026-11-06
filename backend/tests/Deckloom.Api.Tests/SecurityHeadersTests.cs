using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Deckloom.Api.Tests;

// T-077 (RED): cabecalhos de seguranca do design (secao de seguranca, CSP/A9) em toda resposta.
public sealed class SecurityHeadersTests : IClassFixture<SecurityHeadersTests.LocalFactory>
{
    private const string DesignCsp =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data:; "
        + "connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'";

    private const string DesignPermissionsPolicy = "camera=(), microphone=(), geolocation=()";
    private const string SessionCookie = "__Host-dl_session";

    private readonly LocalFactory _local;

    public SecurityHeadersTests(LocalFactory local) => _local = local;

    [Theory]
    [InlineData("/__test/ok")]
    [InlineData("/")]
    public async Task Response_carries_the_design_content_security_policy(string path)
    {
        var response = await Get(_local, path);

        Assert.Equal(DesignCsp, SingleValue(response, "Content-Security-Policy"));
    }

    [Theory]
    [InlineData("/__test/ok")]
    [InlineData("/")]
    public async Task Response_content_security_policy_never_allows_unsafe_inline(string path)
    {
        var response = await Get(_local, path);

        Assert.DoesNotContain("unsafe-inline", SingleValue(response, "Content-Security-Policy"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/__test/ok")]
    [InlineData("/")]
    public async Task Response_has_referrer_policy_no_referrer(string path)
    {
        var response = await Get(_local, path);

        Assert.Equal("no-referrer", SingleValue(response, "Referrer-Policy"));
    }

    [Theory]
    [InlineData("/__test/ok")]
    [InlineData("/")]
    public async Task Response_has_x_content_type_options_nosniff(string path)
    {
        var response = await Get(_local, path);

        Assert.Equal("nosniff", SingleValue(response, "X-Content-Type-Options"));
    }

    [Theory]
    [InlineData("/__test/ok")]
    [InlineData("/")]
    public async Task Response_has_the_design_permissions_policy(string path)
    {
        var response = await Get(_local, path);

        Assert.Equal(DesignPermissionsPolicy, SingleValue(response, "Permissions-Policy"));
    }

    [Fact]
    public async Task Error_response_also_carries_security_headers()
    {
        var response = await Get(_local, "/__test/throw");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(DesignCsp, SingleValue(response, "Content-Security-Policy"));
        Assert.Equal("nosniff", SingleValue(response, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task Hsts_is_absent_in_the_local_environment()
    {
        var response = await Get(_local, "/__test/ok");

        // Exige antes o cabecalho de seguranca presente para nao passar com o middleware ausente.
        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Hsts_is_present_outside_the_local_environment()
    {
        using var factory = new ProductionFactory();

        var response = await Get(factory, "/__test/ok");

        Assert.False(string.IsNullOrWhiteSpace(SingleValue(response, "Strict-Transport-Security")));
    }

    [Fact]
    public async Task Authenticated_response_is_not_cacheable()
    {
        using var client = _local.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__test/ok");
        request.Headers.Add("Cookie", $"{SessionCookie}=token-de-teste");

        var response = await client.SendAsync(request);

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> Get(WebApplicationFactory<Program> factory, string path)
    {
        using var client = factory.CreateClient();
        return await client.GetAsync(path);
    }

    private static string SingleValue(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values) ? Assert.Single(values) : string.Empty;

    public class LocalFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.ConfigureServices(services =>
            {
                services.RemoveDatabaseRoleCheck();
                services.AddSingleton<IStartupFilter, TestEndpointsFilter>();
            });
        }
    }

    public sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSyntheticModuleSettings();
            builder.ConfigureServices(services =>
            {
                services.RemoveDatabaseRoleCheck();
                services.AddSingleton<IStartupFilter, TestEndpointsFilter>();
            });
        }
    }

    private sealed class TestEndpointsFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                next(app);
                app.UseRouting();
                app.UseEndpoints(MapTestEndpoints);
            };

        private static void MapTestEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/__test/ok", () => Results.Ok(new { ok = true }));
            endpoints.MapGet("/__test/throw", (HttpContext _) =>
                throw new InvalidOperationException("falha de teste"));
        }
    }
}
