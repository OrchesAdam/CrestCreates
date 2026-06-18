using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Shared.DataFilter;
using CrestCreates.Infrastructure.DataFilter;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Infrastructure.Permission;

public static class PermissionServiceCollectionExtensions
{
    public static IServiceCollection AddDataFilterServices(this IServiceCollection services)
    {
        services.AddScoped<DataFilterState>();
        services.AddScoped<IDataPermissionFilter, DataPermissionFilter>();
        
        return services;
    }
}
