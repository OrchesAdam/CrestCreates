using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Delivery.Options;
using CrestCreates.Runtime.Delivery.Registration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Runtime.Delivery;

internal sealed class OutboxDispatcher
{
    private readonly IOutboxDispatchStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReadOnlyDictionary<string, Func<IServiceProvider, IOutboxDeliveryHandler>> _handlers;
    private readonly IReadOnlySet<string> _requiredConsumerIds;
    private readonly OutboxDeliveryOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IOutboxDispatchStore store,
        IServiceScopeFactory scopeFactory,
        IEnumerable<OutboxDeliveryHandlerRegistration> handlers,
        IEnumerable<OutboxRequiredConsumerMetadata> requiredConsumers,
        OutboxDeliveryOptions options,
        ILogger<OutboxDispatcher> logger)
    {
        _store = store;
        _scopeFactory = scopeFactory;
        _handlers = handlers.ToDictionary(h => h.ContractId, h => h.Resolve, StringComparer.Ordinal);
        _requiredConsumerIds = requiredConsumers.Select(c => c.ConsumerId).ToHashSet(StringComparer.Ordinal);
        _options = options;
        _options.Validate();
        _logger = logger;
    }

    public async ValueTask<int> DispatchBatchAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var claims = await _store.ClaimAsync(new OutboxClaimRequest
        {
            OwnerId = ownerId,
            BatchSize = _options.BatchSize,
            LeaseDuration = _options.LeaseDuration,
            SupportedContractIds = _handlers.Keys.ToHashSet(StringComparer.Ordinal),
            SupportedRequiredConsumerIds = _requiredConsumerIds
        }, cancellationToken).ConfigureAwait(false);
        var processed = 0;
        foreach (var claim in claims)
        {
            processed++;
            await DispatchClaimAsync(claim, cancellationToken).ConfigureAwait(false);
        }
        return processed;
    }


    private async ValueTask DispatchClaimAsync(OutboxDeliveryClaim claim, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _options.HandlerTimeout;
        OutboxDeliveryOutcome outcome;
        try
        {
            if (claim.Lease.Attempt > _options.MaximumHandlerAttempts)
            {
                await ApplyAsync(
                    _store.DeadLetterAsync(claim.Message.Metadata.MessageId, claim.Lease,
                        new OutboxDeliveryFailure
                        {
                            Code = "DELIVERY_ATTEMPT_BUDGET_EXHAUSTED",
                            Message = "The durable message exceeded the delivery attempt budget before handler invocation.",
                            Retryable = false
                        }, cancellationToken), "DeadLetter", claim);
                return;
            }
            if (!OutboxMessageIntegrity.Matches(claim.Message))
            {
                await ApplyAsync(_store.DeadLetterAsync(claim.Message.Metadata.MessageId, claim.Lease,
                    new OutboxDeliveryFailure
                    {
                        Code = "INTEGRITY_MISMATCH",
                        Message = "The durable outbox payload failed its integrity check.",
                        Retryable = false
                    }, cancellationToken), "DeadLetter", claim);
                return;
            }
            if (!_handlers.TryGetValue(claim.Message.Metadata.ContractId, out var resolver))
            {
                throw new OutboxCompositionException($"No handler is registered for active contract '{claim.Message.Metadata.ContractId}'.");
            }
            using var scope = _scopeFactory.CreateScope();
            var handler = resolver(scope.ServiceProvider);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.HandlerTimeout);
            outcome = await handler.HandleAsync(new OutboxDeliveryContext
            {
                Message = claim.Message,
                Lease = claim.Lease,
                AttemptDeadline = deadline,
                Services = scope.ServiceProvider
            }, timeout.Token).ConfigureAwait(false);
        }
        catch (OutboxCompositionException)
        {
            // Composition is an operational precondition, never a poison-message
            // outcome. Leave the lease untouched so fixing registration makes the
            // same durable fact eligible again.
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            outcome = OutboxDeliveryOutcome.Retry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outbox delivery failed for {MessageId}", claim.Message.Metadata.MessageId);
            outcome = OutboxDeliveryOutcome.Retry;
        }

        switch (outcome)
        {
            case OutboxDeliveryOutcome.Accepted:
            case OutboxDeliveryOutcome.Duplicate:
                await ApplyAsync(_store.AckAsync(claim.Message.Metadata.MessageId, claim.Lease, cancellationToken), "Ack", claim);
                break;
            case OutboxDeliveryOutcome.Conflict:
                await ApplyAsync(_store.DeadLetterAsync(claim.Message.Metadata.MessageId, claim.Lease,
                    new OutboxDeliveryFailure { Code = "CONSUMER_CONFLICT", Message = "Consumer rejected a changed durable fact.", Retryable = false }, cancellationToken), "DeadLetter", claim);
                break;
            default:
                var delay = _options.GetRetryDelay(claim.Lease.Attempt);
                var next = DateTimeOffset.UtcNow + delay;
                if (claim.Lease.Attempt >= _options.MaximumHandlerAttempts)
                    await ApplyAsync(_store.DeadLetterAsync(claim.Message.Metadata.MessageId, claim.Lease,
                        new OutboxDeliveryFailure { Code = "ATTEMPT_BUDGET_EXHAUSTED", Message = "Maximum delivery attempts exhausted.", Retryable = false }, cancellationToken), "DeadLetter", claim);
                else
                    await ApplyAsync(_store.RetryAsync(claim.Message.Metadata.MessageId, claim.Lease,
                        new OutboxDeliveryFailure { Code = "HANDLER_RETRY", Message = "Handler requested retry.", Retryable = true }, next, cancellationToken), "Retry", claim);
                break;
        }
    }

    private async ValueTask ApplyAsync(ValueTask<OutboxDeliveryMutationResult> mutation, string operation, OutboxDeliveryClaim claim)
    {
        var result = await mutation.ConfigureAwait(false);
        if (result is OutboxDeliveryMutationResult.StaleLease or OutboxDeliveryMutationResult.NotFound)
        {
            _logger.LogDebug("Outbox {Operation} did not apply for {MessageId}: {Result}", operation, claim.Message.Metadata.MessageId, result);
            return;
        }
        if (result is OutboxDeliveryMutationResult.Conflict)
            throw new OutboxCompositionException($"Outbox {operation} conflicted for '{claim.Message.Metadata.MessageId}'.");
    }
}
