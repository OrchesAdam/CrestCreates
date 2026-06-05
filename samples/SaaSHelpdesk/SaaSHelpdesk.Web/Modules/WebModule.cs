using CrestCreates.Data.EFCore;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SaaSHelpdesk.EntityFrameworkCore.Modules;
using System.Threading.Tasks;

namespace SaaSHelpdesk.Web.Modules;

[CrestModule(typeof(EntityFrameworkCoreModule), Order = 0)]
public class WebModule : ModuleBase
{
    public override async Task OnApplicationInitializationAsync(IHost host)
    {
        // Run framework host migration + identity seeding + application data seeding
        var runner = host.Services.GetRequiredService<HostMigrationAndSeedRunner>();
        await runner.RunAsync(host.Services);
    }
}