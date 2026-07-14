using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Bootstrap;
using CrestCreates.Capability.Internal;
using CrestCreates.Capability.Middleware;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
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

        builder.Use<AuditMiddleware>();           // Outermost — records all outcomes
        builder.Use<RateLimitMiddleware>();
        builder.Use<TenantMiddleware>();
        builder.Use<AuthorizationMiddleware>();
        builder.Use<ValidationMiddleware>();
        builder.Use<IdempotencyMiddleware>();
        builder.Use<MetricsMiddleware>();          // Wraps Handler
        builder.Use<EventPublishingMiddleware>();

        configure?.Invoke(builder);

        services.TryAddSingleton(builder);
        services.TryAddScoped<ICapabilityPipeline, CapabilityPipeline>();
        services.TryAddScoped<ICapabilityAuthorizationService, PermissionCapabilityAuthorizationService>();
        services.TryAddTransient<AuditMiddleware>();       // New
        services.TryAddTransient<RateLimitMiddleware>();
        services.TryAddTransient<TenantMiddleware>();
        services.TryAddTransient<AuthorizationMiddleware>();
        services.TryAddTransient<ValidationMiddleware>();
        services.TryAddTransient<IdempotencyMiddleware>();
        services.TryAddTransient<MetricsMiddleware>();
        services.TryAddTransient<EventPublishingMiddleware>();

        // Register the single static resolver instance for both the concrete
        // type and the interface so that DI resolution always returns the same
        // object regardless of which service type is requested.
        var concreteResolver = CapabilityHandlerResolverProvider.GetConcreteResolver();
        var interfaceResolver = CapabilityHandlerResolverProvider.GetResolver();
        services.TryAddSingleton<CapabilityHandlerResolver>(_ => concreteResolver);
        services.TryAddSingleton<ICapabilityHandlerResolver>(_ => interfaceResolver);

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

    public static IServiceCollection AddCapabilityRuntime(
        this IServiceCollection services)
    {
        services.AddCapabilityPipeline();

        // Dispatcher + Resolver
        services.TryAddScoped<ICapabilityDispatcher>(sp =>
            new CapabilityDispatcher(
                sp.GetRequiredService<ICapabilityResolver>(),
                sp.GetRequiredService<ICapabilityPipeline>(),
                sp.GetService<ITenantContext>(),
                sp.GetService<ICurrentUser>()));
        services.TryAddSingleton<ICapabilityResolver, DefaultCapabilityResolver>();
        services.TryAddSingleton<ICapabilityVersionResolver, DefaultCapabilityVersionResolver>();

        // Audit — default NoOp
        services.TryAddSingleton<ICapabilityAuditStore, NullCapabilityAuditStore>();

        // Bootstrap Validators
        services.AddSingleton<IBootstrapValidator, CapabilityHandlerValidator>();
        services.AddSingleton<IBootstrapValidator, CapabilitySchemaValidator>();

        // Capability Registry (for binding status contributors)
        services.TryAddSingleton<ICapabilityRegistry, CapabilityRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<CapabilityDescriptor>,
            RegistryValidationEngine<CapabilityDescriptor>>();

        // Generated handler registrations are additive via CapabilityHandlerResolverProvider.Register().
        // The static resolver is the single source of truth.
        // Resolver registration is already done in AddCapabilityPipeline() so that
        // AddCapabilityPipeline() remains independently usable.
        // AddCapabilityRuntime() only adds dispatcher, resolver services, and bootstrap.

        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, CapabilityBindingStatusContributor>();

        // Relationship Extractor
        services.AddSingleton<IDescriptorRelationshipExtractor, CapabilityRelationshipExtractor>();

        return services;
    }

    public static IServiceCollection AddInMemoryCapabilityAudit(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<ICapabilityAuditStore, InMemoryCapabilityAuditStore>());
        return services;
    }
}
