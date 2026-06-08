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

        builder.Use<AuthorizationMiddleware>();
        builder.Use<ValidationMiddleware>();

        configure?.Invoke(builder);

        services.TryAddSingleton(builder);
        services.TryAddSingleton<ICapabilityPipeline, CapabilityPipeline>();
        services.TryAddTransient<AuthorizationMiddleware>();
        services.TryAddTransient<ValidationMiddleware>();

        return services;
    }
}
