using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Registration;
using CrestCreates.Metadata.Abstractions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.RabbitMQ.Composition;

/// <summary>
/// Deterministic composition step, registered before the consumer by AddRabbitMqEventBus:
/// 1) builds the existing EventRegistry from the registered IEventDescriptorProvider set when it has not
///    been built yet (Build is idempotent; a kernel coordinator may have built it already);
/// 2) freezes the RabbitMQ binding registry — the entire declaration validation runs here, before any
///    broker I/O exists;
/// 3) signals the consumer readiness gate. Failure faults the gate first, then fails startup.
/// </summary>
internal sealed class RabbitMqCompositionHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMqCompositionReadiness _readiness;
    private readonly ILogger<RabbitMqCompositionHostedService> _logger;

    public RabbitMqCompositionHostedService(
        IServiceProvider serviceProvider,
        RabbitMqCompositionReadiness readiness,
        ILogger<RabbitMqCompositionHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _readiness = readiness;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var eventRegistry = _serviceProvider.GetRequiredService<EventRegistry>();
            if (eventRegistry.State != RegistryState.Built)
            {
                eventRegistry.Build(_serviceProvider.GetServices<IEventDescriptorProvider>());
            }

            var registry = _serviceProvider.GetRequiredService<IRabbitMqBindingRegistry>();
            _logger.LogInformation(
                "RabbitMQ composition frozen: {Publishable} publishable events, {Queues} queue plan(s), publisher-only: {PublisherOnly}",
                registry.PublishableEventTypes.Count,
                registry.QueuePlans.Count,
                registry.PublisherOnly);
            _readiness.SignalReady();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _readiness.Fail(ex);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
