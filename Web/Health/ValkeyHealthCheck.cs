using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Web.Health;

public class ValkeyHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public ValkeyHealthCheck(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var latency = await db.PingAsync();
            return HealthCheckResult.Healthy($"Valkey responded in {latency.TotalMilliseconds:F1}ms");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Valkey ping failed.", ex);
        }
    }
}
