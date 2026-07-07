using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DynamicApi;

internal static class CapabilityEndpointMapper
{
    public static void MapEndpoint(
        IEndpointRouteBuilder endpoints,
        CapabilityEndpointDescriptor descriptor,
        CapabilityDescriptor capability,
        CapabilityEndpointBindingContract binding)
    {
        var httpMethod = descriptor.HttpMethod.ToString().ToUpperInvariant();

        var routeHandler = endpoints.MapMethods(
            descriptor.RoutePattern,
            new[] { httpMethod },
            async (HttpContext context) =>
            {
                var input = await binding.BindInputAsync(context, context.RequestAborted);

                var dispatcher = context.RequestServices
                    .GetRequiredService<ICapabilityDispatcher>();
                var result = await dispatcher.DispatchAsync(
                    capability, InvocationSource.Http, input,
                    ctx =>
                    {
                        ctx.CausationId = context.TraceIdentifier;
                        ctx.IdempotencyKey = ResolveIdempotencyKey(context);
                        ctx.Items["HttpTraceIdentifier"] = context.TraceIdentifier;
                        ctx.Items["CapabilityEndpointId"] = descriptor.Id;
                    },
                    context.RequestAborted);

                return CapabilityEndpointResultMapper.Map(result, descriptor.OutputMapping);
            });

        // Apply endpoint metadata
        routeHandler.WithDisplayName($"{descriptor.Capability.Id} → {descriptor.RoutePattern}");

        if (descriptor.Projection.Tags is { Count: > 0 } tags)
            routeHandler.WithTags(tags.ToArray());
        if (descriptor.Projection.OperationId is not null)
            routeHandler.WithName(descriptor.Projection.OperationId);

        // 8a only applies Tags and OperationId to Minimal API metadata.
        // GroupName/Summary/Description/Deprecated/Visibility are stored in
        // Projection metadata for future OpenAPI integration.

        // Authorization
        if (descriptor.AuthorizationMode == CapabilityEndpointAuthorizationMode.RequireAuthenticated)
            routeHandler.RequireAuthorization();
        else if (descriptor.AuthorizationMode == CapabilityEndpointAuthorizationMode.AllowAnonymous)
            routeHandler.AllowAnonymous();
    }

    private static string ResolveIdempotencyKey(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("Idempotency-Key", out var key)
            && !string.IsNullOrWhiteSpace(key))
            return key!;
        return Guid.NewGuid().ToString("N");
    }
}
