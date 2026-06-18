using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrestCreates.HealthCheck.Services;

public interface IHealthCheckService
{
    Task<HealthReport> CheckHealthAsync(CancellationToken cancellationToken = default);
    Task<HealthReport> CheckHealthAsync(string[] tags, CancellationToken cancellationToken = default);
}