using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.HumanTask;

internal sealed class HumanTaskCompletedOutboxHandler : IOutboxDeliveryHandler
{
    private readonly IOutboxRequiredConsumerResolver<HumanTaskCompletedEvent> _resolver;
    private readonly OptionalCompatibilityExecutionTracker _tracker;
    private readonly ILogger<HumanTaskCompletedOutboxHandler>? _logger;

    public HumanTaskCompletedOutboxHandler(
        IOutboxRequiredConsumerResolver<HumanTaskCompletedEvent> resolver,
        OptionalCompatibilityExecutionTracker tracker,
        ILogger<HumanTaskCompletedOutboxHandler>? logger = null)
    { _resolver = resolver; _tracker = tracker; _logger = logger; }

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
        var scope = context.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var eventBus = scope.ServiceProvider.GetService<ILocalEventBus>();
        if (eventBus is null)
        {
            scope.Dispose();
            return OutboxDeliveryOutcome.Accepted;
        }
        var remaining = context.AttemptDeadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            scope.Dispose();
            return OutboxDeliveryOutcome.Accepted;
        }
        var compatibility = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        compatibility.CancelAfter(remaining);
        var execution = eventBus.PublishAsync(payload, compatibility.Token);
        var completed = await Task.WhenAny(execution, Task.Delay(remaining, cancellationToken)).ConfigureAwait(false);
        if (completed == execution)
        {
            try { await execution.ConfigureAwait(false); }
            catch (Exception exception) { _logger?.LogWarning(exception, "Optional HumanTask completion LocalEvent compatibility lane failed for {MessageId}.", context.Message.Metadata.MessageId); }
            compatibility.Dispose();
            scope.Dispose();
        }
        else if (!_tracker.TryTrack(execution, scope, compatibility))
        {
            _logger?.LogWarning("Optional HumanTask compatibility tracker is full; skipping detached work for {MessageId}.", context.Message.Metadata.MessageId);
            compatibility.Dispose();
            scope.Dispose();
        }
        return OutboxDeliveryOutcome.Accepted;
    }
}
