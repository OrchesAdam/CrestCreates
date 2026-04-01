using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;

namespace CrestCreates.HealthCheck.Mvc.Modules;

public class HealthCheckMvcModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        base.OnConfigureServices(services);

        services.AddControllers();
        services.AddEndpointsApiExplorer();
    }

    public override void OnApplicationInitialization(IHost host)
    {
        base.OnApplicationInitialization(host);

        var app = host.Services.GetRequiredService<IApplicationBuilder>();
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}