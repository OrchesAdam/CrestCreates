using CrestCreates.Data.EFCore;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LibraryManagement.EntityFrameworkCore.Modules;
using System.Threading.Tasks;

namespace LibraryManagement.Web.Modules;

[CrestModule(typeof(EntityFrameworkCoreModule), Order = 0)]
public class WebModule : ModuleBase
{
    public override async Task OnApplicationInitializationAsync(IHost host)
    {
        var runner = host.Services.GetRequiredService<HostMigrationAndSeedRunner>();
        await runner.RunAsync(host.Services);
    }
}