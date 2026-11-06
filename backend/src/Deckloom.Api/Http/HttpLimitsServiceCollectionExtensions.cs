using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Deckloom.Api.Http;

public static class HttpLimitsServiceCollectionExtensions
{
    private static readonly PathString AuthPrefix = new("/api/v1/auth");

    // Limites de corpo e limitador de concorrencia por IP (REQ-032). Rejeicao: 503 sem corpo, cookie ou Retry-After.
    public static IServiceCollection AddHttpLimits(this IServiceCollection services)
    {
        services.AddOptions<HttpLimitsOptions>().BindConfiguration(HttpLimitsOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AuthConcurrencyOptions>().BindConfiguration(AuthConcurrencyOptions.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<AuthConcurrencyOptions>>((limiter, concurrency) =>
        {
            var settings = concurrency.Value;
            limiter.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path.StartsWithSegments(AuthPrefix)
                    ? RateLimitPartition.GetConcurrencyLimiter(
                        ClientIpKey.From(context),
                        _ => new ConcurrencyLimiterOptions
                        {
                            PermitLimit = settings.PermitLimit,
                            QueueLimit = settings.QueueLimit,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        })
                    : RateLimitPartition.GetNoLimiter(ClientIpKey.Unknown));
        });
        return services;
    }
}
