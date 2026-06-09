using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Capability;

public static class CapabilityServiceCollectionExtensions
{
    public static IServiceCollection AddCapabilityPipeline(
        this IServiceCollection services,
        Action<CapabilityPipelineBuilder>? configure = null)
    {
        var builder = new CapabilityPipelineBuilder();

        builder.Use<RateLimitMiddleware>();
        builder.Use<TenantMiddleware>();
        builder.Use<AuthorizationMiddleware>();
        builder.Use<ValidationMiddleware>();
        builder.Use<IdempotencyMiddleware>();
        builder.Use<EventPublishingMiddleware>();
        builder.Use<MetricsMiddleware>();

        configure?.Invoke(builder);

        services.TryAddSingleton(builder);
        services.TryAddSingleton<CapabilityHandlerResolver>();
        services.TryAddSingleton<ICapabilityHandlerResolver>(sp => sp.GetRequiredService<CapabilityHandlerResolver>());
        services.TryAddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        services.TryAddTransient<RateLimitMiddleware>();
        services.TryAddTransient<TenantMiddleware>();
        services.TryAddTransient<AuthorizationMiddleware>();
        services.TryAddTransient<ValidationMiddleware>();
        services.TryAddTransient<IdempotencyMiddleware>();
        services.TryAddTransient<EventPublishingMiddleware>();
        services.TryAddTransient<MetricsMiddleware>();

        return services;
    }

    /// <summary>
    /// Registers a handler invoker for a capability name using a DelegateHandlerInvoker.
    /// Prefer using the source generator (HandlerInvokerSourceGenerator) which emits
    /// strongly-typed wrapper classes at compile time with zero reflection.
    /// </summary>
    public static IServiceCollection AddHandlerInvoker(
        this IServiceCollection services,
        string capabilityName,
        Func<object?, CancellationToken, Task<object?>> handler)
    {
        var invoker = new DelegateHandlerInvoker(handler);
        services.AddSingleton<ICapabilityHandlerInvoker>(invoker);
        return services;
    }
}
