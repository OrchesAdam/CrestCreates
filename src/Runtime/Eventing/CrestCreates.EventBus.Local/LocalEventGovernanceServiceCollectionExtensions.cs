using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Hosted governance readiness gate. Registered (with the existing EventRegistryBootstrapper and
/// BootstrapCoordinator) exactly when the host composes governance for the local event chain;
/// admission waits on it before any governed execution. A freeze/assembly failure faults the gate
/// and fails host startup deterministically.
/// </summary>
public sealed class LocalEventGovernanceReadiness
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitAsync(CancellationToken cancellationToken) => _ready.Task.WaitAsync(cancellationToken);

    public void SignalReady() => _ready.TrySetResult();

    public void Fail(Exception exception) => _ready.TrySetException(exception);
}

/// <summary>
/// Readiness task of the local governance chain. Runs after the existing event-registry
/// bootstrapper in the shared coordinator topology (never waits for another hosted service):
/// a zero descriptor-provider assembly is an actionable assembly error, not a ready host.
/// </summary>
internal sealed class LocalGovernanceReadinessBootstrapTask : IBootstrapTask
{
    internal const string EventRegistryTaskId = "event-registry";
    internal const string ReadinessTaskId = "local-governance-readiness";

    public string TaskId => ReadinessTaskId;

    public Type ServiceType => typeof(LocalGovernanceReadinessBootstrapTask);

    public IReadOnlyList<string> Dependencies { get; } = new[] { EventRegistryTaskId };

    public bool IsRequired => true;

    public Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken ct)
    {
        var readiness = serviceProvider.GetRequiredService<LocalEventGovernanceReadiness>();
        try
        {
            var descriptorProviders = serviceProvider.GetServices<IEventDescriptorProvider>();
            var hasProvider = false;
            foreach (var _ in descriptorProviders)
            {
                hasProvider = true;
                break;
            }

            if (!hasProvider)
            {
                throw new InvalidOperationException(
                    "This host declares governance-required local events but no IEventDescriptorProvider is " +
                    "registered; register the descriptor providers of the owning assemblies (for example by calling " +
                    "the generated AddEventDescriptors() extension) before composing governance. A zero-provider " +
                    "registry is an assembly error, not a ready host.");
            }

            var eventRegistry = serviceProvider.GetRequiredService<EventRegistry>();
            if (eventRegistry.State != CrestCreates.Metadata.Abstractions.Registry.RegistryState.Built)
            {
                throw new InvalidOperationException(
                    "The event registry is not built after the event-registry bootstrap task; governance readiness " +
                    "cannot be signalled.");
            }

            readiness.SignalReady();
            return Task.CompletedTask;
        }
        catch (Exception failure)
        {
            readiness.Fail(failure);
            throw;
        }
    }
}

public static class LocalEventGovernanceServiceCollectionExtensions
{
    /// <summary>
    /// Composes the governance readiness chain for local events: the existing event-registry
    /// bootstrapper, the existing bootstrap coordinator and the readiness task. Called by the
    /// generated AddLocalEvents() of assemblies that declare governance-required events, or
    /// explicitly by hosts using manual declarations. Plain notification hosts must not call this.
    /// </summary>
    public static IServiceCollection AddCrestLocalEventGovernance(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEventKernel();
        services.TryAddSingleton<LocalEventGovernanceReadiness>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBootstrapTask, EventRegistryBootstrapper>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, BootstrapCoordinator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBootstrapTask, LocalGovernanceReadinessBootstrapTask>());
        return services;
    }
}
