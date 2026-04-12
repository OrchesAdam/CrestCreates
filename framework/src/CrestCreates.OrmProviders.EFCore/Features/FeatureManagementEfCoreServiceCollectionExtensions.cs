using CrestCreates.Domain.Features;
using CrestCreates.OrmProviders.EFCore.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.OrmProviders.EFCore.Features;

public static class FeatureManagementEfCoreServiceCollectionExtensions
{
    public static IServiceCollection AddFeatureManagementEfCore(this IServiceCollection services)
    {
        services.TryAddScoped<IFeatureRepository, FeatureRepository>();
        return services;
    }
}
