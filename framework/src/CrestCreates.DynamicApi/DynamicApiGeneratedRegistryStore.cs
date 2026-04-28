using System.Collections.Concurrent;
using Microsoft.AspNetCore.Routing;

namespace CrestCreates.DynamicApi;

public static class DynamicApiGeneratedRegistryStore
{
    private static readonly ConcurrentDictionary<string, IDynamicApiGeneratedProvider> Providers = new(StringComparer.Ordinal);

    public static void Register(IDynamicApiGeneratedProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Providers.TryAdd(provider.GetType().FullName ?? provider.GetType().Name, provider);
    }

    public static IReadOnlyCollection<IDynamicApiGeneratedProvider> GetProviders()
    {
        return Providers.Values.ToArray();
    }

    public static DynamicApiRegistry? BuildRegistry(DynamicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var registries = GetProviders()
            .Select(provider => provider.CreateRegistry(options))
            .Where(registry => registry.Services.Count > 0)
            .ToArray();

        if (registries.Length == 0)
        {
            return null;
        }

        return new DynamicApiRegistry(registries.SelectMany(registry => registry.Services).ToArray());
    }

    public static DynamicApiRegistry BuildRequiredRegistry(DynamicApiOptions options)
    {
        return BuildRegistry(options) ?? throw CreateMissingGeneratedProviderException(options);
    }

    public static bool MapGeneratedEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(options);

        var mapped = false;
        foreach (var provider in GetProviders())
        {
            var registry = provider.CreateRegistry(options);
            if (registry.Services.Count == 0)
            {
                continue;
            }

            provider.MapEndpoints(endpoints, options);
            mapped = true;
        }

        return mapped;
    }

    public static InvalidOperationException CreateMissingGeneratedProviderException(DynamicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var assemblies = options.ServiceAssemblies.Count == 0
            ? "未配置 ServiceAssemblies"
            : string.Join(", ", options.ServiceAssemblies.Select(assembly => assembly.GetName().Name));

        return new InvalidOperationException(
            $"Dynamic API 未找到编译期生成的 provider，当前主链只支持生成链。ServiceAssemblies: {assemblies}。如需临时诊断，可显式启用 {nameof(DynamicApiOptions.UseRuntimeReflectionFallback)}。");
    }
}