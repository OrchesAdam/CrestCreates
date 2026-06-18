using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Event;

public sealed class EventRegistryBootstrapper : IHostedService, IBootstrapTask
{
    private readonly EventRegistry _registry;
    private readonly IEnumerable<IEventDescriptorProvider> _providers;

    public EventRegistryBootstrapper(
        EventRegistry registry,
        IEnumerable<IEventDescriptorProvider> providers)
    {
        _registry = registry;
        _providers = providers;
    }

    // IBootstrapTask
    public string TaskId => "event-registry";
    public Type ServiceType => typeof(EventRegistryBootstrapper);
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();
    public bool IsRequired => true;

    public Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken ct)
    {
        _registry.Build(_providers);
        return Task.CompletedTask;
    }

    // IHostedService (preserved for backward compat)
    public Task StartAsync(CancellationToken ct)
    {
        _registry.Build(_providers);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
