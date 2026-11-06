using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Deckloom.Api.Tests;

// Console.SetOut e global: estes testes nao rodam em paralelo com outras classes.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostLoggingSerial
{
    public const string Name = "HostLogging";
}

// T-079 (RED, host): logs estruturados em JSON no stdout com traceId e sem corpo de requisicao.
[Collection(HostLoggingSerial.Name)]
public sealed class HostLoggingTests
{
    private const string BodySentinel = "corpo-sentinela-123";

    [Fact]
    public async Task NFR_logs_are_written_to_stdout_as_json_lines_with_trace_id()
    {
        var stdout = new StringWriter();
        var original = Console.Out;
        Console.SetOut(stdout);
        try
        {
            using var factory = new LoggingFactory(new ConcurrentQueue<string>());
            using var client = factory.CreateClient();
            stdout.GetStringBuilder().Clear();

            await client.GetAsync("/__test/log-ok");
            var lines = await WaitForRequestLines(stdout, "/__test/log-ok");

            Assert.NotEmpty(lines);
            foreach (var line in lines)
            {
                var root = ParseJsonLine(line);

                // QA-23: o logger de console grava em fila propria, entao a partida do host pode chegar depois do
                // Clear acima; essas linhas sao JSON, mas nao pertencem a uma requisicao e nao tem traceId.
                if (root.GetProperty("category").GetString() == "Microsoft.Hosting.Lifetime")
                {
                    continue;
                }

                Assert.True(root.TryGetProperty("traceId", out var traceId), $"linha sem traceId: {line}");
                Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
            }
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task NFR_request_body_is_never_written_to_any_log()
    {
        var stdout = new StringWriter();
        var captured = new ConcurrentQueue<string>();
        var original = Console.Out;
        Console.SetOut(stdout);
        try
        {
            using var factory = new LoggingFactory(captured);
            using var client = factory.CreateClient();
            using var content = new StringContent(
                $"{{\"password\":\"{BodySentinel}\"}}", Encoding.UTF8, "application/json");

            await client.PostAsync("/__test/log-ok", content);
            await WaitForRequestLines(stdout, "/__test/log-ok");
            await Task.Delay(300);

            Assert.DoesNotContain(BodySentinel, stdout.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(captured, m => m.Contains(BodySentinel, StringComparison.Ordinal));
            // Garante que o host de fato logou a requisicao (senao a ausencia do marcador nao prova nada).
            Assert.Contains("/__test/log-ok", stdout.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static JsonElement ParseJsonLine(string line)
    {
        try
        {
            return JsonDocument.Parse(line).RootElement;
        }
        catch (JsonException)
        {
            Assert.Fail($"linha de log nao e JSON: {line}");
            throw;
        }
    }

    private static async Task<string[]> WaitForRequestLines(StringWriter stdout, string marker)
    {
        for (var i = 0; i < 30; i++)
        {
            var lines = SplitLines(stdout.ToString());
            if (lines.Any(l => l.Contains(marker, StringComparison.Ordinal)))
            {
                await Task.Delay(200);
                return SplitLines(stdout.ToString());
            }

            await Task.Delay(100);
        }

        return SplitLines(stdout.ToString());
    }

    private static string[] SplitLines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private sealed class LoggingFactory(ConcurrentQueue<string> captured) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSyntheticModuleSettings();
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(new CapturingProvider(captured));
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveDatabaseRoleCheck();
                services.AddSingleton<IStartupFilter, LogEndpointFilter>();
            });
        }
    }

    private sealed class LogEndpointFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                next(app);
                app.UseRouting();
                app.UseEndpoints(Map);
            };

        private static void Map(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/__test/log-ok", () => Results.Ok());
            endpoints.MapPost("/__test/log-ok", () => Results.Ok());
        }
    }

    private sealed class CapturingProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Enqueue(formatter(state, exception) + " " + (state?.ToString() ?? string.Empty));
    }
}
