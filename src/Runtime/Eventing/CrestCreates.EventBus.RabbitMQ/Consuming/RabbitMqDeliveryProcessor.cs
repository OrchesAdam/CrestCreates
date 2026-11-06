using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CrestCreates.EventBus.RabbitMQ.Registration;
using CrestCreates.EventBus.RabbitMQ.Serialization;

namespace CrestCreates.EventBus.RabbitMQ.Consuming;

/// <summary>
/// Delivery preflight and handler execution: envelope decode, wire-identity resolution against the
/// frozen registry, queue-plan membership, payload decode and the whole-batch handler run inside one
/// async scope. It performs no broker I/O; the consumer maps the outcome to settlement.
/// </summary>
internal sealed class RabbitMqDeliveryProcessor
{
    private readonly Func<IRabbitMqBindingRegistry?> _registryAccessor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RabbitMqConsumer> _logger;

    public RabbitMqDeliveryProcessor(
        Func<IRabbitMqBindingRegistry?> registryAccessor,
        IServiceScopeFactory scopeFactory,
        ILogger<RabbitMqConsumer> logger)
    {
        _registryAccessor = registryAccessor;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<RabbitMqDeliveryOutcome> ProcessAsync(
        RabbitMqConsumer.DeliveryContext delivery,
        CancellationToken cancellationToken)
    {
        RabbitMqMessageEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(
                delivery.Body,
                RabbitMqMessageEnvelopeContext.Default.RabbitMqMessageEnvelope);
        }
        catch (JsonException ex)
        {
            return new RabbitMqDeliveryOutcome.Rejected($"malformed envelope JSON ({ex.Message})");
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.EventType))
        {
            return new RabbitMqDeliveryOutcome.Rejected("envelope has no event type");
        }

        if (string.IsNullOrEmpty(envelope.Payload))
        {
            // JSON envelopes may legally carry payload:null; without this check the typed
            // deserialize would throw ArgumentNullException, bypass every rejection branch and
            // leave the delivery unacknowledged forever.
            return new RabbitMqDeliveryOutcome.Rejected("envelope has no payload");
        }

        var registry = _registryAccessor()
            ?? throw new InvalidOperationException(
                "The RabbitMQ binding registry is not resolved; the consumer has not completed startup.");

        var resolution = registry.ResolveWire(envelope.EventType, envelope.EventId, envelope.EventVersion);
        if (!resolution.Success)
        {
            return new RabbitMqDeliveryOutcome.Rejected(resolution.FailureReason!);
        }

        var binding = resolution.Binding!;
        var identity = new RabbitMqWireIdentity(binding.Descriptor.Id, binding.Descriptor.Version);
        if (!delivery.Plan.HandlersByIdentity.TryGetValue(identity, out var handlers) || handlers.Count == 0)
        {
            return new RabbitMqDeliveryOutcome.Rejected(
                $"queue '{delivery.Plan.Queue}' does not declare event '{binding.Descriptor.Name}' v{binding.Descriptor.Version}");
        }

        object? payload;
        try
        {
            payload = JsonSerializer.Deserialize(envelope.Payload, binding.Codec);
        }
        catch (JsonException ex)
        {
            return new RabbitMqDeliveryOutcome.Rejected(
                $"payload JSON does not match '{binding.Descriptor.PayloadType.FullName}' ({ex.Message})");
        }

        if (payload is null)
        {
            return new RabbitMqDeliveryOutcome.Rejected("payload deserialized to null");
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            // Resolve every handler for this delivery before running any of them: a missing
            // registration or a failing constructor must not leave partial business effects
            // (freeze already validates that every handler type is a registered service; this
            // guards scope-construction failures and keeps the delivered batch transactional in
            // the "all or nothing per attempt" sense).
            var handlerInstances = new object[handlers.Count];
            for (var index = 0; index < handlers.Count; index++)
            {
                handlerInstances[index] = scope.ServiceProvider.GetRequiredService(handlers[index].HandlerType);
            }

            for (var index = 0; index < handlers.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await handlers[index].Invoker(handlerInstances[index], payload, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Delivery of '{EventName}' on queue {Queue} was cancelled during shutdown and remains unacknowledged.",
                binding.Descriptor.Name, delivery.Plan.Queue);
            return new RabbitMqDeliveryOutcome.Cancelled();
        }
        catch (Exception ex)
        {
            return new RabbitMqDeliveryOutcome.BusinessFailure(binding, ex);
        }

        return new RabbitMqDeliveryOutcome.Acked(binding.Descriptor.Name);
    }
}

/// <summary>Business/handler outcome of one delivery; broker settlement is applied by the consumer.</summary>
internal abstract record RabbitMqDeliveryOutcome
{
    public sealed record Acked(string EventName) : RabbitMqDeliveryOutcome;

    public sealed record Rejected(string Reason) : RabbitMqDeliveryOutcome;

    public sealed record BusinessFailure(RabbitMqPublishBinding Binding, Exception Exception)
        : RabbitMqDeliveryOutcome;

    public sealed record Cancelled : RabbitMqDeliveryOutcome;
}
