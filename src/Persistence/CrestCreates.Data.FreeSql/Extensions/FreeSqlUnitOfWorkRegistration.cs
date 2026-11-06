using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CrestCreates.Data.Abstractions;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Data.Core;
using CrestCreates.Data.FreeSql.UnitOfWork;

namespace CrestCreates.Data.FreeSql.Extensions;

/// <summary>
/// FreeSql assembly core: a scoped unit of work, its provider binding and the single typed
/// current-user adapter. Modules and both extension overloads delegate here.
/// </summary>
internal static class FreeSqlUnitOfWorkRegistration
{
    public static IServiceCollection AddFreeSqlUnitOfWorkCore(this IServiceCollection services)
    {
        services.TryAddScoped<ICurrentUserProvider>(sp =>
            sp.GetService<ICurrentUser>() is { } currentUser
                ? new CurrentUserProviderAdapter(currentUser)
                : new DefaultCurrentUserProvider());
        services.AddScoped<FreeSqlUnitOfWork>();
        services.AddUnitOfWorkProvider(
            OrmProvider.FreeSql,
            static sp => sp.GetRequiredService<FreeSqlUnitOfWork>(),
            supportsRequiresNew: false,
            capabilities: new UnitOfWorkProviderCapabilities
            {
                PromptTerminationOnAbandon = true,
                DiscardUncommittedOnAbandon = true
            });

        return services;
    }
}
