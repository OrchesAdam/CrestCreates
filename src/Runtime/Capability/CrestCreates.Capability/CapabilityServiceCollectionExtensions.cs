using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Bootstrap;
using CrestCreates.Capability.Internal;
using CrestCreates.Capability.Middleware;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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

        // Stable hash — required by CapabilityPipeline for descriptor hashing
        services.AddDescriptorStableHash();

        services.TryAddSingleton(builder);
        services.TryAddScoped<ICapabilityPipeline, CapabilityPipeline>();
        services.TryAddScoped<CapabilityExecutionContextAccessor>();
        services.TryAddScoped<ICapabilityExecutionContextAccessor>(sp =>
            sp.GetRequiredService<CapabilityExecutionContextAccessor>());
        services.TryAddScoped<ICapabilityAuthorizationService, PermissionCapabilityAuthorizationService>();
        services.TryAddTransient<AuditMiddleware>();       // New
        services.TryAddTransient<RateLimitMiddleware>();
        services.TryAddTransient<TenantMiddleware>();
        services.TryAddTransient<AuthorizationMiddleware>();
        services.TryAddTransient<ValidationMiddleware>();
        services.TryAddSingleton<ICapabilityInputValidationPolicy,
            AllowUnknownCapabilityInputPropertiesPolicy>();
        services.TryAddTransient<IdempotencyMiddleware>();
        services.TryAddTransient<MetricsMiddleware>();
        services.TryAddTransient<EventPublishingMiddleware>();

        // Compatibility resolver for legacy generated providers. Explicitly
        // selected generated modules replace this registration with a Host-
        // owned resolver through their generated Apply(IServiceCollection)
        // method before the Host is built.
        //
        // New pattern: ICapabilityHandlerModule instances are collected and
        // the composed resolver is built from them in alphabetical order by Id.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICapabilityHandlerModule>(
                LegacyCapabilityHandlerModule.Instance));

        services.TryAddSingleton<CapabilityHandlerResolver>(sp =>
        {
            var resolver = new CapabilityHandlerResolver();
            var modules = sp.GetServices<ICapabilityHandlerModule>()
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .ToList();
            foreach (var module in modules)
            {
                module.Apply(resolver);
            }
            return resolver;
        });

        services.TryAddSingleton<ICapabilityHandlerResolver>(sp =>
            sp.GetRequiredService<CapabilityHandlerResolver>());

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

        // Bootstrap Validators
        services.AddSingleton<IBootstrapValidator, CapabilityHandlerValidator>();
        services.AddSingleton<IBootstrapValidator, CapabilitySchemaValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBootstrapValidator, CapabilityAccountabilityCompositionValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CapabilityAccountabilityCompositionValidator>());

        // Capability Registry (for binding status contributors)
        services.TryAddSingleton<ICapabilityRegistry, CapabilityRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<CapabilityDescriptor>,
            RegistryValidationEngine<CapabilityDescriptor>>();

        // Generated modules are applied explicitly by their Add* extension.
        // The compatibility resolver above remains available only to legacy
        // generated consumers during migration and is never selected by the
        // Memory Tool module.

        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, CapabilityBindingStatusContributor>();

        // Relationship Extractor
        services.AddSingleton<IDescriptorRelationshipExtractor, CapabilityRelationshipExtractor>();

        return services;
    }

    [Obsolete("Compatibility-only append store. It is not an Accountability sink and does not participate in the runtime mainline.")]
    public static IServiceCollection AddInMemoryCapabilityAudit(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<ICapabilityAuditStore, InMemoryCapabilityAuditStore>());
        return services;
    }
}
