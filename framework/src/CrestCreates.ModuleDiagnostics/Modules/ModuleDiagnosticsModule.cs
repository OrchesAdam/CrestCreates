using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using CrestCreates.ModuleDiagnostics.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.ModuleDiagnostics.Modules;

[CrestModule]
public class ModuleDiagnosticsModule : ModuleBase
{
    /// <summary>
    /// The shared diagnostics store instance. Set during ConfigureServices,
    /// read by generated ModuleAutoInitializer code.
    /// </summary>
    public static ModuleDiagnosticsStore Store { get; } = new();

    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModuleDiagnosticsStore>(Store);
    }
}