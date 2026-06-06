using CrestCreates.ModuleDiagnostics.Stores;
using CrestCreates.ModuleDiagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrestCreates.ModuleDiagnostics.Modules;

/// <summary>
/// Non-module DI registration helper for the module diagnostics store and health check.
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
        services.AddSingleton<IHealthCheck, ModuleHealthCheck>();
        return services;
    }
}