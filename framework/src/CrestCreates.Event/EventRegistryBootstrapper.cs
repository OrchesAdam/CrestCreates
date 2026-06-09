using CrestCreates.Event.Abstractions;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Event;

public sealed class EventRegistryBootstrapper : IHostedService
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

    public Task StartAsync(CancellationToken ct)
    {
        _registry.Build(_providers);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
