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

internal static class HealthReportResponseMapper
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
            Description = entry.Value.Description,
            Exception = entry.Value.Exception?.Message,
            Data = entry.Value.Data.Count > 0 ? new HealthReportData(entry.Value.Data) : null,
            Tags = entry.Value.Tags.ToList()
        };
    }
}
