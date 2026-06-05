using CrestCreates.Data.EFCore;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LibraryManagement.EntityFrameworkCore.Modules;

namespace LibraryManagement.Web.Modules;

[CrestModule(typeof(EntityFrameworkCoreModule), Order = 0)]
public class WebModule : ModuleBase
{
    public override void OnApplicationInitialization(IHost host)
    {
        var runner = host.Services.GetRequiredService<HostMigrationAndSeedRunner>();
        runner.RunAsync(host.Services).GetAwaiter().GetResult();
    }
}