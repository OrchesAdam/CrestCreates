using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.Core;

[CrestModule]
public class DataCoreModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        // Shared data infrastructure registrations go here.
        // Currently a placeholder — populated as common data concerns are extracted.
    }
}