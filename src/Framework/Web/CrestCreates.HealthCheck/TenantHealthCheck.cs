using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories.Permission;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestCreates.HealthCheck;

public class TenantHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    private readonly ILogger<TenantHealthCheck> _logger;

    public TenantHealthCheck(IServiceProvider serviceProvider, ILogger<TenantHealthCheck>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? NullLogger<TenantHealthCheck>.Instance;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var resolvingRepository = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = _serviceProvider.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
            resolvingRepository = false;
            await repository.AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy("TENANT_STORE_REACHABLE");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failureCode = exception switch
            {
                TimeoutException => "TIMEOUT",
                System.Net.Sockets.SocketException => "CONNECTION_FAILURE",
                InvalidOperationException or ArgumentException when resolvingRepository => "CONFIGURATION_FAILURE",
                _ => "PROVIDER_FAILURE"
            };
            // Log only controlled classification and type metadata, never repository exception details.
            _logger.LogWarning(new EventId(17603, "TENANT_PROBE_FAILED"),
                "Health probe {Probe} failed: {FailureCode} ({ExceptionType})",
                "Tenant", failureCode, exception.GetType().Name);
            return HealthCheckResult.Unhealthy("TENANT_STORE_UNREACHABLE");
        }
    }
}
