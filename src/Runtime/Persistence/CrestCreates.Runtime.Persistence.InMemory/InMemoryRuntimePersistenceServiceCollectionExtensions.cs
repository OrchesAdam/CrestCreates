using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Providers;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.InMemory.Kernel;
using CrestCreates.Runtime.Persistence.InMemory.Stores;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;
using CrestCreates.Runtime.Persistence.InMemory.Bootstrap;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;

namespace CrestCreates.Runtime.Persistence.InMemory;

public static class InMemoryRuntimePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCreatesInMemoryRuntimePersistence(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBootstrapTask, InMemoryRuntimeSchemaCompatibilityBootstrapTask>());
        services.TryAddSingleton<InMemoryRuntimeTransactionAccessor>();
        services.TryAddSingleton<InMemoryRuntimeTransactionCoordinator>();
        services.TryAddSingleton<IRuntimeTransactionCoordinator>(sp => sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>());
        services.TryAddSingleton<IRuntimePersistenceProviderCapabilities, InMemoryRuntimeProviderCapabilities>();
        services.TryAddSingleton<IDescriptorSnapshotPersistenceHasher, DescriptorSnapshotPersistenceHasher>();
        services.TryAddSingleton<IWorkflowInstanceStore>(sp =>
            new InMemoryWorkflowInstanceStore(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        services.TryAddSingleton<IHumanTaskInstanceStore>(sp =>
            new InMemoryHumanTaskInstanceStore(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        services.TryAddSingleton<IDescriptorSnapshotStore>(sp =>
            new InMemoryDescriptorSnapshotStore(
                sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>(),
                sp.GetRequiredService<IDescriptorSnapshotPersistenceHasher>()));
        services.TryAddSingleton<IWorkflowSuspensionReceiptStore>(sp =>
            new InMemoryWorkflowSuspensionReceiptStore(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        services.TryAddSingleton<IWorkflowAbortReceiptStore>(sp =>
            new InMemoryWorkflowAbortReceiptStore(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        services.TryAddSingleton<ITransactionalOutboxWriter>(sp => new InMemoryTransactionalOutboxWriter(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        services.TryAddSingleton<IOutboxDispatchStore>(sp => new InMemoryOutboxDispatchStore(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        services.TryAddSingleton<IWorkflowContinuationAcceptanceStore>(sp => new InMemoryWorkflowContinuationAcceptanceStore(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        services.TryAddSingleton<IOutboxCompositionProbe>(sp => new InMemoryOutboxCompositionProbe(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        services.TryAddSingleton<IHumanTaskCompletionObligationPreflight>(sp => new InMemoryHumanTaskCompletionObligationPreflight(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        return services;
    }
}
