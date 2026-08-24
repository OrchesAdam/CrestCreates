using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// Event handler that processes HumanTask completion events for
/// descriptor activation review tasks. Parses the review decision
/// from the HumanTask result, enriches it with TenantId/CorrelationId
/// from the HumanTask instance, and routes it to the activation review orchestrator.
/// </summary>
public sealed class DescriptorActivationReviewHumanTaskEventHandler
    : IOutboxRequiredConsumer<HumanTaskCompletedEvent>
{
    public const string ConsumerIdValue = "crest.agent-control-plane.activation-review/v1";
    public string ConsumerId => ConsumerIdValue;
    private readonly IActivationReviewOrchestrator _orchestrator;
    private readonly IHumanTaskInstanceStore _humanTaskInstanceStore;
    private readonly ILogger<DescriptorActivationReviewHumanTaskEventHandler> _logger;
    private readonly IRuntimeStateContractRegistry _stateRegistry;

    public DescriptorActivationReviewHumanTaskEventHandler(
        IActivationReviewOrchestrator orchestrator,
        IHumanTaskInstanceStore humanTaskInstanceStore,
        ILogger<DescriptorActivationReviewHumanTaskEventHandler> logger,
        IRuntimeStateContractRegistry stateRegistry)
    {
        _orchestrator = orchestrator;
        _humanTaskInstanceStore = humanTaskInstanceStore;
        _logger = logger;
        _stateRegistry = stateRegistry;
    }

    public async Task HandleAsync(HumanTaskCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        // Only process activation review HumanTasks
        if (@event.HumanTaskPin.Ref.Id != DescriptorActivationHumanTaskIds.ActivationReview)
        {
            return;
        }

        _logger.LogInformation(
            "Processing activation review completion for HumanTask {TaskInstanceId}, outcome: {Outcome}",
            @event.HumanTaskKey.InstanceId, @event.Outcome);

        // Parse the review decision from the event result
        var result = @event.Result is null ? null : _stateRegistry.Restore(@event.Result);
        if (!DescriptorActivationReviewDecisionParser.TryParseReviewDecision(
            result, out var parsedDecision, out var error))
        {
            throw new InvalidOperationException(
                $"Failed to parse activation review decision from HumanTask '{@event.HumanTaskKey.InstanceId}': {error}");
        }

        // Enrich the decision with TenantId/CorrelationId from the HumanTask instance
        var enrichedDecision = await EnrichDecisionAsync(
            parsedDecision!, @event.HumanTaskKey, cancellationToken);

        if (enrichedDecision is null)
        {
            throw new InvalidOperationException($"HumanTask '{@event.HumanTaskKey.InstanceId}' is unavailable for activation review enrichment.");
        }

        // Route the enriched decision to the orchestrator
        await _orchestrator.ProcessReviewDecisionAsync(enrichedDecision, cancellationToken);
    }

    public async ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(
        HumanTaskCompletedEvent payload,
        OutboxDeliveryContext context,
        CancellationToken cancellationToken = default)
    {
        await HandleAsync(payload, cancellationToken).ConfigureAwait(false);
        return OutboxRequiredConsumerResult.Accepted();
    }

    private async Task<DescriptorActivationReviewDecision?> EnrichDecisionAsync(
        DescriptorActivationReviewDecision parsedDecision,
        RuntimeInstanceKey humanTaskKey,
        CancellationToken cancellationToken)
    {
        // If TenantId/CorrelationId are already non-empty, use them as-is
        if (!string.IsNullOrEmpty(parsedDecision.TenantId)
            && !string.IsNullOrEmpty(parsedDecision.CorrelationId))
        {
            return parsedDecision;
        }

        // Look up the HumanTask instance to obtain TenantId and the task input (CorrelationId)
        var instance = await _humanTaskInstanceStore.GetAsync(humanTaskKey, cancellationToken);
        if (instance is null)
        {
            _logger.LogError(
                "HumanTask instance '{InstanceId}' not found — cannot enrich activation review decision.",
                humanTaskKey.InstanceId);
            return null;
        }

        var tenantId = !string.IsNullOrEmpty(parsedDecision.TenantId)
            ? parsedDecision.TenantId
            : instance.TenantId ?? string.Empty;

        var correlationId = !string.IsNullOrEmpty(parsedDecision.CorrelationId)
            ? parsedDecision.CorrelationId
            : instance.Input is null
                ? string.Empty
                : (_stateRegistry.Restore<DescriptorActivationReviewTaskInput>(instance.Input)).CorrelationId ?? string.Empty;

        return parsedDecision with
        {
            TenantId = tenantId,
            CorrelationId = correlationId
        };
    }
}
