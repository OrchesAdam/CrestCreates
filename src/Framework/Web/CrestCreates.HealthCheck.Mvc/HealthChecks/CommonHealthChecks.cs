using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(Func<System.Data.Common.DbConnection> connectionFactory, TimeSpan? timeout = null,
        ILogger<DatabaseHealthCheck>? logger = null)
    {
        _logger = logger ?? NullLogger<DatabaseHealthCheck>.Instance;
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        if (_timeout <= TimeSpan.Zero || _timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var creatingConnection = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            deadline.CancelAfter(_timeout);
            await using var connection = _connectionFactory();
            creatingConnection = false;
            // Await the provider before disposing its connection, even if it ignores cancellation.
            await connection.OpenAsync(deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            return HealthCheckResult.Healthy("DATABASE_REACHABLE");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failureCode = exception switch
            {
                OperationCanceledException when deadline.IsCancellationRequested => "DEADLINE_EXCEEDED",
                TimeoutException => "TIMEOUT",
                System.Net.Sockets.SocketException => "CONNECTION_FAILURE",
                ArgumentException when creatingConnection => "CONFIGURATION_FAILURE",
                _ => "PROVIDER_FAILURE"
            };
            // Deliberately omit the exception object and all provider-supplied text.
            _logger.LogWarning(new EventId(17601, "DATABASE_PROBE_FAILED"),
                "Health probe {Probe} failed: {FailureCode} ({ExceptionType})",
                "Database", failureCode, exception.GetType().Name);
            return HealthCheckResult.Unhealthy("DATABASE_UNREACHABLE");
        }
    }
}

[HealthCheck(Name = "Redis", Tags = new[] { "redis", "cache", "infrastructure" }, Description = "Check Redis connectivity")]
public class RedisHealthCheck : IHealthCheck
{
    private readonly StackExchange.Redis.IConnectionMultiplexer _connection;
    private readonly TimeSpan _timeout;

    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(StackExchange.Redis.IConnectionMultiplexer connection, TimeSpan? timeout = null,
        ILogger<RedisHealthCheck>? logger = null)
    {
        _logger = logger ?? NullLogger<RedisHealthCheck>.Instance;
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
        catch (Exception exception)
        {
            var failureCode = exception switch
            {
                TimeoutException or StackExchange.Redis.RedisTimeoutException => "TIMEOUT",
                StackExchange.Redis.RedisConnectionException or System.Net.Sockets.SocketException => "CONNECTION_FAILURE",
                ArgumentException => "CONFIGURATION_FAILURE",
                _ => "PROVIDER_FAILURE"
            };
            _logger.LogWarning(new EventId(17602, "REDIS_PROBE_FAILED"),
                "Health probe {Probe} failed: {FailureCode} ({ExceptionType})",
                "Redis", failureCode, exception.GetType().Name);
            return HealthCheckResult.Unhealthy("REDIS_UNREACHABLE");
        }
    }
}