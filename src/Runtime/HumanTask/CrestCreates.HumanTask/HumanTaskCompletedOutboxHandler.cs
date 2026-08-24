using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrestCreates.HumanTask;

internal sealed class HumanTaskCompletedOutboxHandler : IOutboxDeliveryHandler
{
    private readonly IOutboxRequiredConsumerResolver<HumanTaskCompletedEvent> _resolver;
    private readonly ILocalEventBus? _eventBus;
    private readonly ILogger<HumanTaskCompletedOutboxHandler>? _logger;

    public HumanTaskCompletedOutboxHandler(
        IOutboxRequiredConsumerResolver<HumanTaskCompletedEvent> resolver,
        ILocalEventBus? eventBus = null,
        ILogger<HumanTaskCompletedOutboxHandler>? logger = null)
    { _resolver = resolver; _eventBus = eventBus; _logger = logger; }

    public string ContractId => HumanTaskDeliveryConstants.CompletedContractId;

    public async ValueTask<OutboxDeliveryOutcome> HandleAsync(OutboxDeliveryContext context, CancellationToken cancellationToken = default)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize(
                context.Message.Payload,
                HumanTaskJsonSerializerContext.Default.HumanTaskCompletedEvent)
            ?? throw new InvalidOperationException("HumanTask completion payload was empty or invalid.");
        foreach (var consumerId in context.Message.Metadata.RequiredConsumerIds.Order(StringComparer.Ordinal))
        {
            var consumer = _resolver.Resolve(context.Services, consumerId);
            var result = await consumer.ConsumeAsync(payload, context, cancellationToken).ConfigureAwait(false);
            if (result.Outcome is OutboxDeliveryOutcome.Retry or OutboxDeliveryOutcome.Conflict)
                return result.Outcome;
        }
        if (_eventBus is not null)
        {
            var remaining = context.AttemptDeadline - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                using var compatibility = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                compatibility.CancelAfter(remaining);
                try
                {
                    await _eventBus.PublishAsync(payload, compatibility.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger?.LogWarning(exception, "Optional HumanTask completion LocalEvent compatibility lane failed for {MessageId}.", context.Message.Metadata.MessageId);
                }
            }
        }
        return OutboxDeliveryOutcome.Accepted;
    }
}
