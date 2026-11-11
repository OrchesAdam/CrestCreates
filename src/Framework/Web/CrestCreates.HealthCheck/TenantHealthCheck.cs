using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories.Permission;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrestCreates.HealthCheck;

public class TenantHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public TenantHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = _serviceProvider.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
            await repository.AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy("TENANT_STORE_REACHABLE");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("TENANT_STORE_UNREACHABLE");
        }
    }
}
