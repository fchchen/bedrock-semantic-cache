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

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return _redis.IsConnected 
            ? Task.FromResult(HealthCheckResult.Healthy()) 
            : Task.FromResult(HealthCheckResult.Unhealthy());
    }
}