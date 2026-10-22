using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlSugar;
using CrestCreates.Data.Abstractions;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Data.Core;
using CrestCreates.Data.SqlSugar.UnitOfWork;

namespace CrestCreates.Data.SqlSugar.Extensions
{
    /// <summary>
    /// SqlSugar assembly core: one scoped client (and its ISqlSugarClient view), one scoped unit of
    /// work, and the single typed current-user adapter. Modules and extensions delegate here.
    /// </summary>
    public static class SqlSugarServiceCollectionExtensions
    {
        public static IServiceCollection AddSqlSugarWithUow(
            this IServiceCollection services,
            ConnectionConfig connectionConfig)
        {
            ArgumentNullException.ThrowIfNull(connectionConfig);
            return services.AddSqlSugarWithUow(() => new SqlSugarClient(connectionConfig));
        }

        public static IServiceCollection AddSqlSugarWithUow(
            this IServiceCollection services,
            Func<ISqlSugarClient> clientFactory)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(clientFactory);

            services.AddScoped<ISqlSugarClient>(_ => clientFactory());
            services.AddScoped(sp => (SqlSugarClient)sp.GetRequiredService<ISqlSugarClient>());
            services.TryAddScoped<ICurrentUserProvider>(sp =>
            sp.GetService<ICurrentUser>() is { } currentUser
                ? new CurrentUserProviderAdapter(currentUser)
                : new DefaultCurrentUserProvider());
            services.AddScoped<SqlSugarUnitOfWork>();
            services.AddUnitOfWorkProvider(
                OrmProvider.SqlSugar,
                static sp => sp.GetRequiredService<SqlSugarUnitOfWork>(),
                supportsRequiresNew: false,
                capabilities: new UnitOfWorkProviderCapabilities
                {
                    PromptTerminationOnAbandon = true,
                    DiscardUncommittedOnAbandon = true
                });

            return services;
        }
    }
}
