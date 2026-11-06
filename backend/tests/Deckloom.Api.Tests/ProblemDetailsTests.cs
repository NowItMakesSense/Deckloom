using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Deckloom.Api.Problems;
using Deckloom.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Deckloom.Api.Tests;

// T-076 (RED): contrato de erros RFC 9457 do design (secao Contracts).
public sealed class ProblemDetailsTests : IClassFixture<ProblemDetailsTests.ApiFactory>
{
    private const string ProblemJson = "application/problem+json";
    private const string SqlLeak = "SELECT * FROM accounts.player WHERE email = 'vazado@example.com'";

    private static readonly string[] RequiredOnly = ["required"];
    private static readonly string[] PasswordCodes = ["too_short", "invalid_characters"];

    private readonly ApiFactory _factory;

    public ProblemDetailsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task REQ_003_validation_error_returns_400_problem_json_with_stable_shape()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/__test/domain-error?code=required&field=email");
        var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        Assert.Equal("https://deckloom.app/problems/validation_failed", body.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));
        Assert.Equal(400, body.GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData("required")]
    [InlineData("invalid_format")]
    [InlineData("too_short")]
    [InlineData("too_long")]
    [InlineData("length")]
    [InlineData("invalid_characters")]
    [InlineData("invalid")]
    [InlineData("unavailable")]
    [InlineData("version_mismatch")]
    public async Task REQ_003_validation_error_lists_field_code_under_errors(string fieldCode)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/__test/domain-error?code={fieldCode}&field=displayName");
        var body = await ReadJson(response);

        var codes = body.GetProperty("errors").GetProperty("displayName").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Equal([fieldCode], codes);
    }

    [Fact]
    public async Task REQ_003_validation_error_groups_several_codes_per_field()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/__test/domain-errors");
        var body = await ReadJson(response);

        var errors = body.GetProperty("errors");
        Assert.Equal(RequiredOnly, errors.GetProperty("acceptTerms").EnumerateArray().Select(e => e.GetString()!));
        Assert.Equal(
            PasswordCodes,
            errors.GetProperty("password").EnumerateArray().Select(e => e.GetString()!));
    }

    [Fact]
    public async Task REQ_003_problem_details_carry_w3c_trace_id()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/__test/domain-error?code=required&field=email");
        var body = await ReadJson(response);

        var traceId = body.GetProperty("traceId").GetString();
        Assert.NotNull(traceId);
        Assert.Matches(new Regex("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$"), traceId);
    }

    [Fact]
    public async Task REQ_003_trace_id_follows_incoming_traceparent()
    {
        using var client = _factory.CreateClient();
        const string traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__test/domain-error?code=required&field=email");
        request.Headers.Add("traceparent", traceparent);

        var response = await client.SendAsync(request);
        var body = await ReadJson(response);

        Assert.Contains("4bf92f3577b34da6a3ce929d0e0e4736", body.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task REQ_003_unhandled_exception_returns_generic_500_internal_error()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/__test/throw");
        var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("internal_error", body.GetProperty("code").GetString());
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.True(body.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task REQ_003_unhandled_exception_body_leaks_no_stack_sql_or_library_message()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/__test/throw");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("SELECT", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vazado@example.com", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("unauthenticated", 401)]
    [InlineData("invalid_credentials", 401)]
    [InlineData("incorrect_password", 401)]
    [InlineData("reauth_required", 403)]
    [InlineData("reauth_expired", 403)]
    [InlineData("pending_not_found", 404)]
    [InlineData("email_unavailable", 409)]
    [InlineData("google_not_completed", 409)]
    [InlineData("no_local_password", 409)]
    [InlineData("concurrency_conflict", 409)]
    [InlineData("reset_link_invalid", 400)]
    public async Task REQ_005_domain_error_without_field_maps_to_status_and_stable_code(string code, int status)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/__test/domain-error?code={code}");
        var body = await ReadJson(response);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal($"https://deckloom.app/problems/{code}", body.GetProperty("type").GetString());
    }

    [Fact]
    public async Task REQ_005_unknown_domain_error_code_never_leaks_and_maps_to_internal_error()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/__test/domain-error?code=codigo_inexistente");
        var body = await ReadJson(response);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("internal_error", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task REQ_003_cors_is_disabled_for_foreign_origin()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__test/domain-error?code=required&field=email");
        request.Headers.Add("Origin", "https://externo.example");

        var response = await client.SendAsync(request);

        // Sem a resposta do contrato nao ha o que afirmar sobre CORS: exige o 400 antes.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(response.Headers, h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task REQ_003_cors_preflight_is_not_answered_with_cors_headers()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/__test/domain-error");
        request.Headers.Add("Origin", "https://externo.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(response.Headers, h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(raw).RootElement;
    }

    // Fabrica de teste: host real + endpoints de teste montados depois do pipeline do host.
    // A troca da persistencia pelos fakes (Deckloom.Accounts.Testing) entra aqui quando o host registrar os repositorios.
    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSyntheticModuleSettings()
                .ConfigureServices(services =>
                {
                    services.RemoveDatabaseRoleCheck();
                    services.AddSingleton<IStartupFilter, TestEndpointsFilter>();
                });
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
            endpoints.MapGet("/__test/throw", (HttpContext _) =>
                throw new InvalidOperationException(SqlLeak));

            endpoints.MapGet("/__test/domain-error", (string code, string? field) =>
                DomainErrorProblemMapper.ToResult([new DomainError(code, field)]));

            endpoints.MapGet("/__test/domain-errors", () =>
                DomainErrorProblemMapper.ToResult(
                [
                    new DomainError("required", "acceptTerms"),
                    new DomainError("too_short", "password"),
                    new DomainError("invalid_characters", "password"),
                ]));
        }
    }
}
