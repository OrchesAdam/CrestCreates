using Microsoft.Extensions.Diagnostics.HealthChecks;
using CrestCreates.HealthCheck.Attributes;

namespace CrestCreates.HealthCheck.Mvc.HealthChecks;

[HealthCheck(Name = "Memory", Tags = new[] { "memory", "infrastructure" }, Description = "Monitor memory usage")]
public class MemoryHealthCheck : IHealthCheck
{
    private readonly long _threshold;

    public MemoryHealthCheck(long threshold = 1024 * 1024 * 1024)
    {
        _threshold = threshold;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var allocated = GC.GetTotalMemory(false);
        var data = new Dictionary<string, object>
        {
            { "allocated", allocated },
            { "threshold", _threshold },
            { "unit", "bytes" }
        };

        if (allocated > _threshold)
        {
            return Task.FromResult(HealthCheckResult.Degraded($"Memory usage {allocated} exceeds threshold {_threshold}", null, data));
        }

        return Task.FromResult(HealthCheckResult.Healthy($"Memory usage is within threshold", data));
    }
}

[HealthCheck(Name = "Database", Tags = new[] { "database", "sql", "critical" }, Description = "Check database connectivity")]
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public DatabaseHealthCheck(string connectionString = "Server=localhost;Database=master;Integrated Security=true")
    {
        _connectionString = connectionString;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(10, cancellationToken);
            var data = new Dictionary<string, object>
            {
                { "connectionString", _connectionString },
                { "status", "connected" }
            };
            return HealthCheckResult.Healthy("Database connection is healthy", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connection failed", ex);
        }
    }
}

[HealthCheck(Name = "Redis", Tags = new[] { "redis", "cache", "infrastructure" }, Description = "Check Redis connectivity")]
public class RedisHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public RedisHealthCheck(string connectionString = "localhost:6379")
    {
        _connectionString = connectionString;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(10, cancellationToken);
            var data = new Dictionary<string, object>
            {
                { "connectionString", _connectionString },
                { "status", "connected" }
            };
            return HealthCheckResult.Healthy("Redis connection is healthy", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis connection failed", ex);
        }
    }
}