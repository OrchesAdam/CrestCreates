using System.Collections.Concurrent;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DynamicApi;

public static class DynamicApiGeneratedRegistryStore
{
    private static readonly ConcurrentDictionary<string, IDynamicApiGeneratedProvider> Providers = new(StringComparer.Ordinal);

    private static readonly ConcurrentBag<Action<IServiceCollection>> ControllerRegistrationCallbacks = new();

    public static void Register(IDynamicApiGeneratedProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Providers.TryAdd(provider.GetType().FullName ?? provider.GetType().Name, provider);
    }

    public static void RegisterControllerConfigurator(Action<IServiceCollection> configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ControllerRegistrationCallbacks.Add(configurator);
    }

    public static void ApplyControllerRegistrations(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var callback in ControllerRegistrationCallbacks)
        {
            callback(services);
        }
    }

    public static IReadOnlyCollection<IDynamicApiGeneratedProvider> GetProviders()
    {
        return Providers.Values.ToArray();
    }

    public static DynamicApiRegistry? BuildRegistry(DynamicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var serviceKeys = new HashSet<string>(StringComparer.Ordinal);
        var services = new List<DynamicApiServiceDescriptor>();
        foreach (var provider in GetProviders())
        {
            if (!TryGetMatchingGeneratedRegistry(provider, options, out var registry))
            {
                continue;
            }

            foreach (var service in registry.Services)
            {
                var key = $"{service.ServiceType.Assembly.FullName}|{service.ServiceType.FullName}|{service.RoutePrefix}";
                if (serviceKeys.Add(key))
                {
                    services.Add(service);
                }
            }
        }

        if (services.Count == 0)
        {
            return null;
        }

        return new DynamicApiRegistry(services);
    }

    public static DynamicApiRegistry BuildRequiredRegistry(DynamicApiOptions options)
    {
        return BuildRegistry(options) ?? throw CreateMissingGeneratedProviderException(options);
    }

    public static IReadOnlyCollection<DynamicApiEndpointDescriptor> GetEndpointDescriptors(DynamicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var descriptorKeys = new HashSet<string>(StringComparer.Ordinal);
        return GetProviders()
            .SelectMany(provider => provider.EndpointDescriptors)
            .Where(descriptor => options.ServiceAssemblies.Count == 0 ||
                                 options.ServiceAssemblies.Contains(descriptor.ServiceType.Assembly))
            .Where(descriptor => descriptorKeys.Add(CreateEndpointDescriptorKey(descriptor)))
            .ToArray();
    }

    public static bool MapGeneratedEndpoints(IEndpointRouteBuilder endpoints, DynamicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(options);

        var mapped = false;
        var mappedEndpointKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in GetProviders())
        {
            if (!TryGetMatchingGeneratedRegistry(provider, options, out _))
            {
                continue;
            }

            var matchingEndpointKeys = provider.EndpointDescriptors
                .Where(descriptor => options.ServiceAssemblies.Count == 0 ||
                                     options.ServiceAssemblies.Contains(descriptor.ServiceType.Assembly))
                .Select(CreateEndpointDescriptorKey)
                .ToArray();
            if (matchingEndpointKeys.Length > 0 &&
                matchingEndpointKeys.All(key => mappedEndpointKeys.Contains(key)))
            {
                continue;
            }

            provider.MapEndpoints(endpoints, options);
            foreach (var key in matchingEndpointKeys)
            {
                mappedEndpointKeys.Add(key);
            }

            mapped = true;
        }

        return mapped;
    }

    private static string CreateEndpointDescriptorKey(DynamicApiEndpointDescriptor descriptor)
    {
        return string.Join(
            "|",
            descriptor.ServiceType.Assembly.FullName,
            descriptor.ServiceType.FullName,
            descriptor.HttpMethod,
            descriptor.RoutePattern);
    }

    private static bool TryGetMatchingGeneratedRegistry(
        IDynamicApiGeneratedProvider provider,
        DynamicApiOptions options,
        out DynamicApiRegistry registry)
    {
        registry = provider.CreateRegistry(options);
        if (registry.Services.Count > 0)
        {
            return true;
        }

        return provider.EndpointDescriptors.Any(descriptor =>
            options.ServiceAssemblies.Count == 0 ||
            options.ServiceAssemblies.Contains(descriptor.ServiceType.Assembly));
    }

    public static InvalidOperationException CreateMissingGeneratedProviderException(DynamicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var assemblies = options.ServiceAssemblies.Count == 0
            ? "未配置 ServiceAssemblies"
            : string.Join(", ", options.ServiceAssemblies.Select(assembly => assembly.GetName().Name));

        return new InvalidOperationException(
            $"Dynamic API 未找到编译期生成的 provider，当前主链只支持生成链。ServiceAssemblies: {assemblies}。请检查 CrestCreates.CodeGenerator 是否作为 analyzer 引用、应用服务程序集是否被当前项目引用、服务是否符合生成规则，以及 GeneratedDynamicApiRegistry.g.cs / GeneratedDynamicApiEndpoints.g.cs 是否生成并参与编译。");
    }
}
