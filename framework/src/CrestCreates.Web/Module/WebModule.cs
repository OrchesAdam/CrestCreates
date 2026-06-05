using System.Threading.Tasks;
using CrestCreates.Data.EFCore;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Web.Module;

[CrestModule]
public class WebModule : ModuleBase
{
    public override async Task OnApplicationInitializationAsync(IHost host)
    {
        var runner = host.Services.GetRequiredService<HostMigrationAndSeedRunner>();
        await runner.RunAsync(host.Services);
    }
}