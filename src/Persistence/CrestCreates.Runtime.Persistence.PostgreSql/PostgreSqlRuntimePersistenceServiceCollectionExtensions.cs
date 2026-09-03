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
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using CrestCreates.Metadata.Abstractions.Bootstrap;

namespace CrestCreates.Runtime.Persistence.PostgreSql;

public static class PostgreSqlRuntimePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCreatesPostgreSqlRuntimePersistence(
        this IServiceCollection services, PostgreSqlRuntimePersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        PostgreSqlRuntimePersistenceOptionsValidator.Validate(options);
        services.AddSingleton(options);
        services.AddSingleton<NpgsqlDataSource>(_ => new NpgsqlSlimDataSourceBuilder(options.ConnectionString)
            .EnableArrays()
            .Build());
        services.AddSingleton<PostgreSqlRuntimeMigrationRunner>();
        services.AddSingleton<PostgreSqlRuntimeSchemaCompatibilityHostedService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PostgreSqlRuntimeSchemaCompatibilityHostedService>());
        services.AddSingleton<IBootstrapTask>(sp => sp.GetRequiredService<PostgreSqlRuntimeSchemaCompatibilityHostedService>());
        services.AddSingleton<PostgreSqlRuntimeTransactionAccessor>();
        services.AddSingleton<PostgreSqlRuntimeTransactionCoordinator>();
        services.AddSingleton<IRuntimeTransactionCoordinator>(sp => sp.GetRequiredService<PostgreSqlRuntimeTransactionCoordinator>());
        services.AddSingleton<IRuntimePersistenceProviderCapabilities, PostgreSqlRuntimeProviderCapabilities>();
        services.AddSingleton<IDescriptorSnapshotPersistenceHasher, DescriptorSnapshotPersistenceHasher>();
        services.AddSingleton<PostgreSqlRuntimeProviderRegistrationMarker>();
        services.AddSingleton<IWorkflowInstanceStore, PostgreSqlWorkflowInstanceStore>();
        services.AddSingleton<IHumanTaskInstanceStore, PostgreSqlHumanTaskInstanceStore>();
        services.AddSingleton<IWorkflowSuspensionReceiptStore, PostgreSqlWorkflowSuspensionReceiptStore>();
        services.AddSingleton<IWorkflowAbortReceiptStore, PostgreSqlWorkflowAbortReceiptStore>();
        services.AddSingleton<ITransactionalOutboxWriter, PostgreSqlTransactionalOutboxWriter>();
        services.AddSingleton<IOutboxDispatchStore, PostgreSqlOutboxDispatchStore>();
        services.AddSingleton<IWorkflowContinuationAcceptanceStore, PostgreSqlWorkflowContinuationAcceptanceStore>();
        services.AddSingleton<IOutboxCompositionProbe, PostgreSqlOutboxCompositionProbe>();
        services.AddSingleton<IHumanTaskCompletionObligationPreflight, PostgreSqlHumanTaskCompletionObligationPreflight>();
        services.AddSingleton<IDescriptorSnapshotStore, PostgreSqlDescriptorSnapshotStore>();
        services.AddSingleton<IAuditSink, PostgreSqlAuditSink>();

        // Replace development participants with durable PostgreSQL participants
        services.RemoveAll<IAgentToolGovernanceAuditor>();
        services.RemoveAll<IAgentToolBudgetGate>();
        services.RemoveAll<IAgentToolInvocationGate>();
        services.RemoveAll<IAgentToolPreDispatchReconciliationStore>();
        services.RemoveAll<IAgentToolInvocationLeaseAbandoner>();
        services.RemoveAll<IAgentToolPreDispatchPersistenceCapabilities>();
        services.AddSingleton<IAgentToolGovernanceAuditor, PostgreSqlAgentToolGovernanceAuditor>();
        services.AddSingleton<PostgreSqlAgentToolGovernanceAuditor>(sp => (PostgreSqlAgentToolGovernanceAuditor)sp.GetRequiredService<IAgentToolGovernanceAuditor>());
        services.AddSingleton<IAgentToolBudgetGate, PostgreSqlAgentToolBudgetGate>();
        services.AddSingleton<PostgreSqlAgentToolBudgetGate>(sp => (PostgreSqlAgentToolBudgetGate)sp.GetRequiredService<IAgentToolBudgetGate>());
        services.AddSingleton<IAgentToolInvocationGate, PostgreSqlAgentToolInvocationGate>();
        services.AddSingleton<PostgreSqlAgentToolInvocationGate>(sp => (PostgreSqlAgentToolInvocationGate)sp.GetRequiredService<IAgentToolInvocationGate>());
        services.AddSingleton<IAgentToolInvocationLeaseAbandoner>(sp => sp.GetRequiredService<IAgentToolInvocationGate>() as IAgentToolInvocationLeaseAbandoner ?? throw new InvalidOperationException("PostgreSQL gate does not implement IAgentToolInvocationLeaseAbandoner"));
        services.AddSingleton<IAgentToolPreDispatchPersistenceCapabilities>(sp => sp.GetRequiredService<IAgentToolInvocationGate>() as IAgentToolPreDispatchPersistenceCapabilities ?? throw new InvalidOperationException("PostgreSQL gate does not implement IAgentToolPreDispatchPersistenceCapabilities"));
        services.AddSingleton<IAgentToolPreDispatchReconciliationStore, PostgreSqlAgentToolPreDispatchReconciliationStore>();
        // Reconciler and accountability producer are registered by the application/runtime layer.
        // Do not remove them here — only replace the durable participants.
        services.AddSingleton<PostgreSqlAgentToolPreDispatchCleanup>();
        return services;
    }
}
