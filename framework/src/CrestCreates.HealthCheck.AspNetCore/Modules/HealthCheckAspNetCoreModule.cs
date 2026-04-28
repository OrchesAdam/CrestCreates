using CrestCreates.Domain.Shared.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CrestCreates.Modularity;

namespace CrestCreates.HealthCheck.AspNetCore.Modules;

[CrestModule]
public class HealthCheckAspNetCoreModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        base.OnConfigureServices(services);

        services.AddHealthChecks();
    }

    public override void OnApplicationInitialization(IHost host)
    {
        base.OnApplicationInitialization(host);

        var app = host.Services.GetRequiredService<IApplicationBuilder>();
        app.UseHealthChecks("/health");
    }
}