using CrestCreates.Runtime.Persistence.Abstractions.Providers;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

public static class PostgreSqlRuntimePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCreatesPostgreSqlRuntimePersistence(
        this IServiceCollection services, PostgreSqlRuntimePersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.AddSingleton<PostgreSqlRuntimeTransactionAccessor>();
        services.AddSingleton<PostgreSqlRuntimeTransactionCoordinator>();
        services.AddSingleton<IRuntimeTransactionCoordinator>(sp => sp.GetRequiredService<PostgreSqlRuntimeTransactionCoordinator>());
        services.AddSingleton<IRuntimePersistenceProviderCapabilities, PostgreSqlRuntimeProviderCapabilities>();
        services.AddSingleton<IWorkflowInstanceStore, PostgreSqlWorkflowInstanceStore>();
        services.AddSingleton<IHumanTaskInstanceStore, PostgreSqlHumanTaskInstanceStore>();
        services.AddSingleton<IAuditSink, PostgreSqlAuditSink>();
        return services;
    }
}
