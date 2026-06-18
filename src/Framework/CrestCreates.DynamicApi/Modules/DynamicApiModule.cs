using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.DynamicApi;

namespace CrestCreates.DynamicApi.Modules;

[CrestModule]
public class DynamicApiModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
    }
}