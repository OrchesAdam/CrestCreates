using System.Threading.Tasks;
using CrestCreates.HealthCheck.AspNetCore.Modules;
using CrestCreates.HealthCheck.AspNetCore.Serialization;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrestCreates.HealthCheck.AspNetCore;

public static class HealthCheckAspNetCoreEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapCrestHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
            {
                ResponseWriter = HealthCheckAspNetCoreResponseWriter.WriteHealthCheckJsonResponse
            })
            .WithMetadata(new SkipTenantResolutionMetadata());

        return endpoints;
    }
}

internal static class HealthCheckAspNetCoreResponseWriter
{
    public static async Task WriteHealthCheckJsonResponse(HttpContext context, HealthReport report)
    {
        var response = HealthReportResponseMapper.FromHealthReport(report);

        context.Response.StatusCode = report.Status == HealthStatus.Healthy ? 200 : 503;
        await context.Response.WriteAsJsonAsync(response, HealthReportJsonContext.Default.HealthReportResponse);
    }
}
