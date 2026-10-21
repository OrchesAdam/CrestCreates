using Microsoft.AspNetCore.Routing;

namespace CrestCreates.DynamicApi;

internal static class LegacyDynamicApiActivationGuard
{
    internal static void ValidateAllowlist(DynamicApiRegistry registry, ILegacyDynamicApiAllowlist allowlist)
    {
        var published = new HashSet<(Type Service, string Action)>();
        foreach (var service in registry.Services)
        {
            var serviceType = service.ServiceType
                ?? throw new InvalidOperationException(
                    $"CCHTTP007: the legacy provider publishes service '{service.ServiceName}' without a CLR "
                    + "contract type. The Host allowlist matches exact CLR types, so a shape-only service "
                    + "cannot be activated.");

            if (!allowlist.TryGetAllowedActions(serviceType, out var allowed))
            {
                throw new InvalidOperationException(
                    $"CCHTTP007: the legacy provider publishes service '{service.ServiceName}' "
                    + $"({Describe(serviceType)}), but the Host allowlist does not declare it. Every legacy "
                    + "service needs an explicit Allow<TService>(...) entry; whole-assembly or namespace "
                    + "activation is not supported.");
            }

            foreach (var action in service.Actions)
            {
                published.Add((serviceType, action.ActionName));
                if (!allowed.Contains(action.ActionName))
                {
                    throw new InvalidOperationException(
                        $"CCHTTP007: the legacy provider publishes '{service.ServiceName}.{action.ActionName}', "
                        + "but that action is not in the Host allowlist. Add it explicitly or move the "
                        + "service to the formal ExplicitHost projection.");
                }
            }
        }

        foreach (var serviceType in allowlist.ServiceTypes)
        {
            if (!allowlist.TryGetAllowedActions(serviceType, out var allowed))
            {
                continue;
            }

            foreach (var action in allowed)
            {
                if (!published.Contains((serviceType, action)))
                {
                    throw new InvalidOperationException(
                        $"CCHTTP007: the Host allowlist declares {Describe(serviceType)} action '{action}', "
                        + "but no generated legacy provider publishes it. Remove the stale entry.");
                }
            }
        }
    }

    internal static void ValidateRoutes(IEndpointRouteBuilder endpoints, DynamicApiRegistry registry)
    {
        var mappedRoutes = RoutePatternCollisionGuard.CollectMappedRoutes(endpoints);
        var plannedRoutes = new HashSet<(string Method, string Route)>();
        foreach (var service in registry.Services)
        {
            foreach (var action in service.Actions)
            {
                var method = (action.HttpMethod ?? "GET").ToUpperInvariant();
                var canonical = RoutePatternCollisionGuard.Canonicalize(action.FullRoute);
                if (!plannedRoutes.Add((method, canonical))
                    || mappedRoutes.Contains((method, canonical))
                    || mappedRoutes.Contains(("*", canonical)))
                {
                    throw new InvalidOperationException(
                        $"CCHTTP005: legacy action '{service.ServiceName}.{action.ActionName}' publishes "
                        + $"{method} '{action.FullRoute}', which is already mapped by another endpoint. "
                        + "A formal/legacy double publish (or duplicate legacy route) is rejected before mapping.");
                }
            }
        }
    }

    private static string Describe(Type serviceType)
        => $"{serviceType.FullName}, {serviceType.Assembly.GetName().Name}";
}
