using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace ConstructionSaaS.Web.Infrastructure;

public sealed class ApiReadinessCheck(IHttpClientFactory clients, IConfiguration config) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var client = clients.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        var api = new Uri(ApiEndpoint.Resolve(config));
        using var response = await client.GetAsync(new Uri(api, "/health/ready"), cancellationToken);
        return response.IsSuccessStatusCode ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("API unavailable.");
    }
}
