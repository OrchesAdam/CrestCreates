using CrestCreates.Data.EFCore;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SaaSHelpdesk.EntityFrameworkCore.Modules;

namespace SaaSHelpdesk.Web.Modules;

[CrestModule(typeof(EntityFrameworkCoreModule), Order = 0)]
public class WebModule : ModuleBase
{
    public override void OnApplicationInitialization(IHost host)
    {
        // Run framework host migration + identity seeding + application data seeding
        var runner = host.Services.GetRequiredService<HostMigrationAndSeedRunner>();
        runner.RunAsync(host.Services).GetAwaiter().GetResult();
    }
}