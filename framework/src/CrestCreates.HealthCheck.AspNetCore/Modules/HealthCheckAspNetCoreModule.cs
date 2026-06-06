using System;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.HealthCheck.AspNetCore.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using CrestCreates.Modularity;

namespace CrestCreates.HealthCheck.AspNetCore.Modules;

[CrestModule]
public class HealthCheckAspNetCoreModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddHealthChecks();
    }

    public override Task OnApplicationInitializationAsync(IHost host)
    {
        if (host is IEndpointRouteBuilder endpointBuilder)
        {
            endpointBuilder.MapHealthChecks("/health", new HealthCheckOptions
            {
                ResponseWriter = WriteHealthCheckJsonResponse
            });
        }
        else
        {
            var appBuilder = host.Services.GetService<IApplicationBuilder>();
            appBuilder?.UseHealthChecks("/health", new HealthCheckOptions
            {
                ResponseWriter = WriteHealthCheckJsonResponse
            });
        }
        return Task.CompletedTask;
    }

    private static async Task WriteHealthCheckJsonResponse(HttpContext context, HealthReport report)
    {
        var response = HealthReportResponseMapper.FromHealthReport(report);

        context.Response.StatusCode = report.Status == HealthStatus.Healthy ? 200 : 503;
        await context.Response.WriteAsJsonAsync(response, HealthReportJsonContext.Default.HealthReportResponse);
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
