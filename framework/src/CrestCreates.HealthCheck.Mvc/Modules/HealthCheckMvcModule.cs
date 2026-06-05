using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;

namespace CrestCreates.HealthCheck.Mvc.Modules;

[CrestModule]
public class HealthCheckMvcModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
    }

    public override Task OnApplicationInitializationAsync(IHost host)
    {
        var app = host.Services.GetRequiredService<IApplicationBuilder>();
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
        return Task.CompletedTask;
    }
}