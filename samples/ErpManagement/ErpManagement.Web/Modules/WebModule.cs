using CrestCreates.Data.EFCore;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using ErpManagement.EntityFrameworkCore.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ErpManagement.Web.Modules;

[CrestModule(typeof(EntityFrameworkCoreModule), Order = 0)]
public class WebModule : ModuleBase
{
    public override async Task OnApplicationInitializationAsync(IHost host)
    {
        var runner = host.Services.GetRequiredService<HostMigrationAndSeedRunner>();
        await runner.RunAsync(host.Services);
    }
}