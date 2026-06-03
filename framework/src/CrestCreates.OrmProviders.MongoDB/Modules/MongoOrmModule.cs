using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.OrmProviders.MongoDB.Modules;

[CrestModule]
public class MongoOrmModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        base.OnConfigureServices(services);
        // MongoDB specific service registrations can be added here
    }
}