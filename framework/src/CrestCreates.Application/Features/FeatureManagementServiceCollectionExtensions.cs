using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Caching;
using CrestCreates.Domain.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Application.Features;

public static class FeatureManagementServiceCollectionExtensions
{
    public static IServiceCollection AddFeatureManagement(this IServiceCollection services)
    {
        services.AddCrestCaching();

        services.TryAddSingleton<IFeatureDefinitionManager, FeatureDefinitionManager>();
        services.TryAddSingleton<FeatureCacheKeyContributor>();
        services.TryAddScoped<FeatureValueTypeConverter>();
        services.TryAddScoped<FeatureValueAppServiceMapper>();
        services.TryAddScoped<IFeatureStore, FeatureStore>();
        services.TryAddScoped<IFeatureValueResolver, FeatureValueResolver>();
        services.TryAddScoped<IFeatureProvider, FeatureProvider>();
        services.TryAddScoped<IFeatureChecker, FeatureChecker>();
        services.TryAddScoped<IFeatureManager, FeatureManager>();
        services.TryAddScoped<IFeatureAuditRecorder, FeatureAuditRecorder>();
        services.TryAddScoped<IFeatureDefinitionAppService, FeatureDefinitionAppService>();
        services.TryAddScoped<IFeatureAppService, FeatureAppService>();
        services.TryAddScoped<FeatureCacheInvalidator>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IFeatureDefinitionProvider, CoreFeatureDefinitionProvider>());

        return services;
    }

    public static IServiceCollection AddFeatureDefinitionProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IFeatureDefinitionProvider
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFeatureDefinitionProvider, TProvider>());
        return services;
    }
}
