using CrestCreates.Caching.Abstractions;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Caching;

[CrestModule]
public class CachingModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddCrestCaching();
    }
}
