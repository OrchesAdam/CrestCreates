using System.Collections.Generic;
using System.Linq;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.HealthCheck.AspNetCore.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CrestCreates.Modularity;

namespace CrestCreates.HealthCheck.AspNetCore.Modules;

[CrestModule]
public class HealthCheckAspNetCoreModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddHealthChecks();
    }
}

public static class HealthReportResponseMapper
{
    public static HealthReportResponse FromHealthReport(HealthReport report)
    {
        return new HealthReportResponse
        {
            Status = report.Status.ToString(),
            TotalDuration = report.TotalDuration,
            Checks = report.Entries.Select(FromEntry).ToList()
        };
    }

    private static HealthCheckEntryResponse FromEntry(KeyValuePair<string, HealthReportEntry> entry)
    {
        return new HealthCheckEntryResponse
        {
            Name = entry.Key,
            Status = entry.Value.Status.ToString(),
            Duration = entry.Value.Duration,
            Code = entry.Value.Status switch
            {
                HealthStatus.Healthy => "HEALTH_CHECK_HEALTHY",
                HealthStatus.Degraded => "HEALTH_CHECK_DEGRADED",
                _ => "HEALTH_CHECK_UNHEALTHY"
            },
            Description = null,
            Exception = null,
            Data = null,
            Tags = entry.Value.Tags.ToList()
        };
    }
}
