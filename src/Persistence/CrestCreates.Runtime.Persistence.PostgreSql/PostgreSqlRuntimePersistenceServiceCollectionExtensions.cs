using CrestCreates.Agent.Tools;
using CrestCreates.Runtime.Persistence.Abstractions.Providers;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

public static class PostgreSqlRuntimePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCreatesPostgreSqlRuntimePersistence(
        this IServiceCollection services, PostgreSqlRuntimePersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        PostgreSqlRuntimePersistenceOptionsValidator.Validate(options);
        services.AddSingleton(options);
        services.AddSingleton<NpgsqlDataSource>(_ => new NpgsqlSlimDataSourceBuilder(options.ConnectionString).Build());
        services.AddSingleton<PostgreSqlRuntimeMigrationRunner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PostgreSqlRuntimeSchemaCompatibilityHostedService>());
        services.AddSingleton<PostgreSqlRuntimeTransactionAccessor>();
        services.AddSingleton<PostgreSqlRuntimeTransactionCoordinator>();
        services.AddSingleton<IRuntimeTransactionCoordinator>(sp => sp.GetRequiredService<PostgreSqlRuntimeTransactionCoordinator>());
        services.AddSingleton<IRuntimePersistenceProviderCapabilities, PostgreSqlRuntimeProviderCapabilities>();
        services.AddSingleton<IWorkflowInstanceStore, PostgreSqlWorkflowInstanceStore>();
        services.AddSingleton<IHumanTaskInstanceStore, PostgreSqlHumanTaskInstanceStore>();
        services.AddSingleton<IWorkflowSuspensionReceiptStore, PostgreSqlWorkflowSuspensionReceiptStore>();
        services.AddSingleton<IDescriptorSnapshotStore, PostgreSqlDescriptorSnapshotStore>();
        services.AddSingleton<IAuditSink, PostgreSqlAuditSink>();

        // Replace development participants with durable PostgreSQL participants
        services.RemoveAll<IAgentToolGovernanceAuditor>();
        services.RemoveAll<IAgentToolBudgetGate>();
        services.RemoveAll<IAgentToolInvocationGate>();
        services.RemoveAll<IAgentToolPreDispatchReconciliationStore>();
        services.RemoveAll<IAgentToolPreDispatchReconciler>();
        services.AddSingleton<IAgentToolGovernanceAuditor, PostgreSqlAgentToolGovernanceAuditor>();
        services.AddSingleton<IAgentToolBudgetGate, PostgreSqlAgentToolBudgetGate>();
        services.AddSingleton<IAgentToolInvocationGate, PostgreSqlAgentToolInvocationGate>();
        services.AddSingleton<IAgentToolPreDispatchReconciliationStore, PostgreSqlAgentToolPreDispatchReconciliationStore>();
        services.AddSingleton<PostgreSqlAgentToolPreDispatchCleanup>();
        return services;
    }
}
