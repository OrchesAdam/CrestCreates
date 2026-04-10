using System.ComponentModel;
using System.Reflection;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.DynamicApi;

internal interface IDynamicApiScanner
{
    DynamicApiRegistry Scan(DynamicApiOptions options);
}

[Obsolete("Runtime reflection scanning is no longer the Dynamic API default path. Use compile-time generated providers instead.")]
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class DynamicApiScanner : IDynamicApiScanner
{
    private readonly DynamicApiRouteConvention _routeConvention;

    public DynamicApiScanner(DynamicApiRouteConvention routeConvention)
    {
        _routeConvention = routeConvention;
    }

    public DynamicApiRegistry Scan(DynamicApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var services = options.ServiceAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetCustomAttribute<CrestServiceAttribute>() is not null)
            .Where(type => type.GetCustomAttribute<DynamicApiIgnoreAttribute>() is null)
            .SelectMany(implementationType => BuildServiceDescriptors(implementationType, options))
            .OrderBy(descriptor => descriptor.RoutePrefix, StringComparer.Ordinal)
            .ToArray();

        return new DynamicApiRegistry(services);
    }

    private IEnumerable<DynamicApiServiceDescriptor> BuildServiceDescriptors(Type implementationType, DynamicApiOptions options)
    {
        var serviceInterfaces = implementationType
            .GetInterfaces()
            .Where(type => type.IsPublic)
            .Where(type => !type.IsGenericType)
            .Where(type => type.Name.EndsWith("AppService", StringComparison.Ordinal))
            .Where(type => type.GetCustomAttribute<DynamicApiIgnoreAttribute>() is null)
            .Distinct()
            .ToArray();

        foreach (var serviceType in serviceInterfaces)
        {
            var serviceName = TrimServiceName(serviceType);
            var routePrefix = _routeConvention.ResolveServiceRoute(serviceType, serviceName, options);
            var actions = BuildActions(serviceType, implementationType, serviceName, routePrefix).ToArray();
            if (actions.Length == 0)
            {
                continue;
            }

            yield return new DynamicApiServiceDescriptor
            {
                ServiceName = serviceName,
                RoutePrefix = routePrefix,
                ServiceType = serviceType,
                ImplementationType = implementationType,
                Actions = actions
            };
        }
    }

    private IEnumerable<DynamicApiActionDescriptor> BuildActions(Type serviceType, Type implementationType, string serviceName, string routePrefix)
    {
        var methodMappings = BuildMethodMappings(serviceType, implementationType);

        foreach (var mapping in methodMappings)
        {
            if (mapping.ServiceMethod.DeclaringType == typeof(IDisposable))
            {
                continue;
            }

            if (mapping.ServiceMethod.GetCustomAttribute<DynamicApiIgnoreAttribute>() is not null)
            {
                continue;
            }

            var actionName = TrimAsyncSuffix(mapping.ServiceMethod.Name);
            var routePattern = _routeConvention.ResolveActionRoute(mapping.ServiceMethod);
            var httpMethod = _routeConvention.ResolveHttpMethod(mapping.ServiceMethod);
            var parameters = _routeConvention.ResolveParameters(mapping.ServiceMethod, routePattern, httpMethod).ToArray();

            yield return new DynamicApiActionDescriptor
            {
                ActionName = actionName,
                DeclaringTypeName = serviceType.Name,
                OperationId = $"{serviceType.Name}_{actionName}",
                RelativeRoute = routePattern,
                HttpMethod = httpMethod,
                ServiceMethod = mapping.ServiceMethod,
                ImplementationMethod = mapping.ImplementationMethod,
                RoutePrefix = routePrefix,
                Parameters = parameters,
                ReturnDescriptor = DynamicApiRouteConvention.ResolveReturnDescriptor(mapping.ServiceMethod),
                Permission = _routeConvention.ResolvePermission(serviceName, mapping.ServiceMethod)
            };
        }
    }

    private static IEnumerable<(MethodInfo ServiceMethod, MethodInfo ImplementationMethod)> BuildMethodMappings(Type serviceType, Type implementationType)
    {
        var seenMethodKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contractType in GetContractTypes(serviceType))
        {
            var interfaceMapping = implementationType.GetInterfaceMap(contractType);
            for (var index = 0; index < interfaceMapping.InterfaceMethods.Length; index++)
            {
                var serviceMethod = interfaceMapping.InterfaceMethods[index];
                var methodKey = CreateMethodKey(serviceMethod);
                if (!seenMethodKeys.Add(methodKey))
                {
                    continue;
                }

                yield return (serviceMethod, interfaceMapping.TargetMethods[index]);
            }
        }
    }

    private static IEnumerable<Type> GetContractTypes(Type serviceType)
    {
        yield return serviceType;

        foreach (var inheritedInterface in serviceType.GetInterfaces().Where(type => type.IsPublic))
        {
            yield return inheritedInterface;
        }
    }

    private static string CreateMethodKey(MethodInfo methodInfo)
    {
        var parameterTypes = methodInfo.GetParameters()
            .Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name);

        return $"{methodInfo.Name}({string.Join(",", parameterTypes)})";
    }

    private static string TrimServiceName(Type serviceType)
    {
        var name = serviceType.Name;
        if (name.StartsWith("I", StringComparison.Ordinal) && name.Length > 1)
        {
            name = name[1..];
        }

        if (name.EndsWith("AppService", StringComparison.Ordinal))
        {
            name = name[..^"AppService".Length];
        }

        return name;
    }

    private static string TrimAsyncSuffix(string methodName)
    {
        return methodName.EndsWith("Async", StringComparison.Ordinal)
            ? methodName[..^"Async".Length]
            : methodName;
    }
}
