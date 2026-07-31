using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Providers;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.InMemory.Kernel;
using CrestCreates.Runtime.Persistence.InMemory.Stores;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Runtime.Persistence.InMemory;

public static class InMemoryRuntimePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCreatesInMemoryRuntimePersistence(this IServiceCollection services)
    {
        services.TryAddSingleton<InMemoryRuntimeTransactionAccessor>();
        services.TryAddSingleton<InMemoryRuntimeTransactionCoordinator>();
        services.TryAddSingleton<IRuntimeTransactionCoordinator>(sp => sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>());
        services.TryAddSingleton<IRuntimePersistenceProviderCapabilities, InMemoryRuntimeProviderCapabilities>();
        services.TryAddSingleton<IWorkflowInstanceStore>(sp =>
            new InMemoryWorkflowInstanceStore(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        services.TryAddSingleton<IHumanTaskInstanceStore>(sp =>
            new InMemoryHumanTaskInstanceStore(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        services.TryAddSingleton<IDescriptorSnapshotStore>(sp =>
            new InMemoryDescriptorSnapshotStore(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        services.TryAddSingleton<IWorkflowSuspensionReceiptStore>(sp =>
            new InMemoryWorkflowSuspensionReceiptStore(sp.GetRequiredService<InMemoryRuntimeTransactionCoordinator>()));
        return services;
    }
}
