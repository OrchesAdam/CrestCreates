using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using CrestCreates.PluginSystem.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.PluginSystem.Modules;

[CrestModule]
public class PluginSystemModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IPluginManager, PluginManager>();
    }
}