using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Delivery.Options;
using CrestCreates.Runtime.Delivery.Registration;
using CrestCreates.Runtime.Delivery.Bootstrap;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Runtime.Delivery;

public static class DeliveryServiceCollectionExtensions
{
    public static IServiceCollection AddRuntimeDelivery(this IServiceCollection services, Action<OutboxDeliveryOptions>? configure = null)
    {
        var options = new OutboxDeliveryOptions();
        configure?.Invoke(options);
        options.Validate();
        services.TryAddSingleton(options);
        services.TryAddSingleton<IOutboxMessageFactory, Message.DefaultOutboxMessageFactory>();
        services.TryAddSingleton<OutboxCompositionValidator>();
        services.TryAddSingleton<OutboxCompositionReadiness>();
        services.TryAddSingleton<OutboxDispatcher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxDurableCompositionCheck, OutboxActiveRequirementsCompositionCheck>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBootstrapTask, OutboxDurableCompositionBootstrapTask>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, BootstrapCoordinator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxCompositionHostedService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxHostedService>());
        return services;
    }

    public static IServiceCollection AddOutboxDeliveryHandler<THandler>(this IServiceCollection services, string contractId)
        where THandler : class, IOutboxDeliveryHandler
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        services.AddScoped<THandler>();
        services.AddSingleton(new OutboxDeliveryHandlerRegistration(contractId, sp => sp.GetRequiredService<THandler>()));
        return services;
    }

    public static IServiceCollection AddOutboxRequiredConsumer<TPayload, TConsumer>(this IServiceCollection services, string consumerId)
        where TConsumer : class, IOutboxRequiredConsumer<TPayload>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);
        services.AddScoped<TConsumer>();
        services.AddSingleton(new OutboxRequiredConsumerRegistration<TPayload>(consumerId, sp => sp.GetRequiredService<TConsumer>()));
        services.AddSingleton(new OutboxRequiredConsumerMetadata(consumerId));
        services.AddSingleton(new OutboxRequiredConsumerValidationRegistration(consumerId, sp =>
        {
            var consumer = sp.GetRequiredService<TConsumer>();
            if (!string.Equals(consumer.ConsumerId, consumerId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Required consumer '{consumerId}' resolved a mismatched ConsumerId.");
        }));
        services.RemoveAll<IOutboxRequiredConsumerResolver<TPayload>>();
        services.AddSingleton<IOutboxRequiredConsumerResolver<TPayload>, OutboxRequiredConsumerResolver<TPayload>>();
        return services;
    }

    private sealed class OutboxHostedService(OutboxDispatcher dispatcher, OutboxDeliveryOptions options, OutboxCompositionReadiness readiness, ILogger<OutboxHostedService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var ownerId = $"outbox-{Environment.ProcessId}-{Guid.NewGuid():N}";
            await readiness.WaitAsync(stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await dispatcher.DispatchBatchAsync(ownerId, stoppingToken).ConfigureAwait(false); }
                catch (OutboxCompositionException ex)
                {
                    readiness.Fail(ex);
                    throw;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "Outbox worker iteration failed."); }
                await Task.Delay(options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class OutboxCompositionHostedService(OutboxCompositionValidator validator) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) { validator.Validate(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
