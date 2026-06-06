using CrestCreates.ModuleDiagnostics.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.ModuleDiagnostics.Modules;

/// <summary>
/// Non-module DI registration helper for the module diagnostics store.
/// Application hosts should call AddModuleDiagnostics() during service configuration.
/// </summary>
public static class ModuleDiagnosticsServiceCollectionExtensions
{
    /// <summary>
    /// The shared diagnostics store instance. Created once and accessible
    /// by generated ModuleAutoInitializer code via fully-qualified reference.
    /// </summary>
    public static ModuleDiagnosticsStore Store { get; } = new();

    public static IServiceCollection AddModuleDiagnostics(this IServiceCollection services)
    {
        services.AddSingleton<IModuleDiagnosticsStore>(Store);
        return services;
    }
}