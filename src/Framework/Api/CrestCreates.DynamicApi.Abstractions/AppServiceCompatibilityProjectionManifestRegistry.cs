using System.Collections.Generic;

namespace CrestCreates.DynamicApi.Abstractions;

/// <summary>
/// Registry for <see cref="IAppServiceCompatibilityProjectionManifestProvider"/> instances.
/// Providers are registered via [ModuleInitializer] during module startup,
/// and consumed by the capability projection runtime to discover legacy-to-capability mappings.
/// </summary>
public static class AppServiceCompatibilityProjectionManifestRegistry
{
    private static readonly List<IAppServiceCompatibilityProjectionManifestProvider> _providers = new();

    /// <summary>
    /// Registers a manifest provider.
    /// Called by generated ModuleInitializer code.
    /// </summary>
    public static void Register(IAppServiceCompatibilityProjectionManifestProvider provider)
    {
        _providers.Add(provider);
    }

    /// <summary>
    /// Returns all registered manifest providers.
    /// </summary>
    public static IReadOnlyList<IAppServiceCompatibilityProjectionManifestProvider> GetProviders() => _providers;

    /// <summary>
    /// Collects all projection entries from every registered provider.
    /// </summary>
    public static IReadOnlyList<AppServiceCompatibilityProjectionEntry> GetAllEntries()
    {
        var entries = new List<AppServiceCompatibilityProjectionEntry>();
        foreach (var provider in _providers)
            entries.AddRange(provider.GetEntries());
        return entries;
    }
}
