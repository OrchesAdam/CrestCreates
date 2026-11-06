using CrestCreates.Accountability.Abstractions.Retention;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

public static class PostgreSqlAuditRetentionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL retention participant. Hosts compose this together with
    /// AddAuditRetention (runtime kernel) and only after applying the additive migration.
    /// </summary>
    public static IServiceCollection AddPostgreSqlAuditRetention(this IServiceCollection services)
    {
        services.TryAddSingleton<IAuditRetentionStore, PostgreSqlAuditRetentionStore>();
        return services;
    }
}
