using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
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
    private readonly OutboxDeliveryOptions _options;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IOutboxDispatchStore store,
        IServiceScopeFactory scopeFactory,
        IEnumerable<OutboxDeliveryHandlerRegistration> handlers,
        OutboxDeliveryOptions options,
        ILogger<OutboxDispatcher> logger)
    {
        _store = store;
        _scopeFactory = scopeFactory;
        _handlers = handlers.ToDictionary(h => h.ContractId, h => h.Resolve, StringComparer.Ordinal);
        _options = options;
        _options.Validate();
        _logger = logger;
    }

    public async ValueTask<int> DispatchBatchAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = await _store.ClaimAsync(new OutboxClaimRequest
        {
            OwnerId = ownerId,
            BatchSize = _options.BatchSize,
            LeaseDuration = _options.LeaseDuration,
            Now = now
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
            if (!OutboxMessageIntegrity.Matches(claim.Message))
            {
                await _store.DeadLetterAsync(claim.Message.Metadata.MessageId, claim.Lease,
                    new OutboxDeliveryFailure
                    {
                        Code = "INTEGRITY_MISMATCH",
                        Message = "The durable outbox payload failed its integrity check.",
                        Retryable = false
                    }, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (!_handlers.TryGetValue(claim.Message.Metadata.ContractId, out var resolver))
            {
                await _store.DeadLetterAsync(claim.Message.Metadata.MessageId, claim.Lease,
                    new OutboxDeliveryFailure { Code = "COMPOSITION_MISSING_HANDLER", Message = "No handler is registered for the message contract.", Retryable = false }, cancellationToken).ConfigureAwait(false);
                return;
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
                await _store.AckAsync(claim.Message.Metadata.MessageId, claim.Lease, cancellationToken).ConfigureAwait(false);
                break;
            case OutboxDeliveryOutcome.Conflict:
                await _store.DeadLetterAsync(claim.Message.Metadata.MessageId, claim.Lease,
                    new OutboxDeliveryFailure { Code = "CONSUMER_CONFLICT", Message = "Consumer rejected a changed durable fact.", Retryable = false }, cancellationToken).ConfigureAwait(false);
                break;
            default:
                var delay = _options.GetRetryDelay(claim.Lease.Attempt);
                var next = DateTimeOffset.UtcNow + delay;
                if (claim.Lease.Attempt >= _options.MaximumHandlerAttempts)
                    await _store.DeadLetterAsync(claim.Message.Metadata.MessageId, claim.Lease,
                        new OutboxDeliveryFailure { Code = "ATTEMPT_BUDGET_EXHAUSTED", Message = "Maximum delivery attempts exhausted.", Retryable = false }, cancellationToken).ConfigureAwait(false);
                else
                    await _store.RetryAsync(claim.Message.Metadata.MessageId, claim.Lease,
                        new OutboxDeliveryFailure { Code = "HANDLER_RETRY", Message = "Handler requested retry.", Retryable = true }, next, cancellationToken).ConfigureAwait(false);
                break;
        }
    }
}
