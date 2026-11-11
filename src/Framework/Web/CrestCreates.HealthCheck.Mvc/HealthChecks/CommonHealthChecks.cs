using Microsoft.Extensions.Diagnostics.HealthChecks;
using CrestCreates.Domain.Shared.Attributes;

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
    private readonly Func<System.Data.Common.DbConnection> _connectionFactory;

    private readonly TimeSpan _timeout;

    public DatabaseHealthCheck(Func<System.Data.Common.DbConnection> connectionFactory, TimeSpan? timeout = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        if (_timeout <= TimeSpan.Zero || _timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_timeout);
            await using var connection = _connectionFactory();
            // Await the provider before disposing its connection, even if it ignores cancellation.
            await connection.OpenAsync(deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            return HealthCheckResult.Healthy("DATABASE_REACHABLE");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("DATABASE_UNREACHABLE");
        }
    }
}

[HealthCheck(Name = "Redis", Tags = new[] { "redis", "cache", "infrastructure" }, Description = "Check Redis connectivity")]
public class RedisHealthCheck : IHealthCheck
{
    private readonly StackExchange.Redis.IConnectionMultiplexer _connection;
    private readonly TimeSpan _timeout;

    public RedisHealthCheck(StackExchange.Redis.IConnectionMultiplexer connection, TimeSpan? timeout = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        if (_timeout <= TimeSpan.Zero || _timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _connection.GetDatabase().PingAsync().WaitAsync(_timeout, cancellationToken);
            return HealthCheckResult.Healthy("REDIS_REACHABLE");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("REDIS_UNREACHABLE");
        }
    }
}