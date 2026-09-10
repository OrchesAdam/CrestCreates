using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// Event handler that processes HumanTask completion events for
/// descriptor activation review tasks. Parses the review decision
/// from the HumanTask result, binds it to the durably completed HumanTask
/// and its persisted input, and routes it to the activation review orchestrator.
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

    public async Task<ActivationReviewDispatchOutcome> HandleAsync(HumanTaskCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.HumanTaskPin is null)
            throw new InvalidOperationException("Activation review completion is missing its HumanTask descriptor pin.");

        // Only process activation review HumanTasks
        if (@event.HumanTaskPin.Ref.Id != DescriptorActivationHumanTaskIds.ActivationReview)
        {
            return ActivationReviewDispatchOutcome.Accepted;
        }

        _logger.LogInformation(
            "Processing activation review completion for HumanTask {TaskInstanceId}, outcome: {Outcome}",
            @event.HumanTaskKey.InstanceId, @event.Outcome);

        // Parse the review decision from the event result
        object? result;
        try
        {
            result = @event.Result is null ? null : _stateRegistry.Restore(@event.Result);
        }
        catch (RuntimeStateContractException exception)
        {
            throw new InvalidOperationException("The persisted activation review result is not a valid runtime state contract.", exception);
        }
        if (!DescriptorActivationReviewDecisionParser.TryParseReviewDecision(
            result, out var parsedDecision, out var error))
        {
            throw new InvalidOperationException(
                $"Failed to parse activation review decision from HumanTask '{@event.HumanTaskKey.InstanceId}': {error}");
        }

        // Bind the untyped completion fact to the durable task before routing it. The
        // callback payload is not an authority for task identity, tenant, outcome, or
        // actor identity: those facts come from the completed HumanTask and its input.
        var enrichedDecision = await EnrichDecisionAsync(
            parsedDecision!, @event, cancellationToken);

        // Route the enriched decision to the orchestrator
        return await _orchestrator.ProcessReviewDecisionAsync(enrichedDecision, @event.EventId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(
        HumanTaskCompletedEvent payload,
        OutboxDeliveryContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var outcome = await HandleAsync(payload, cancellationToken).ConfigureAwait(false);
            return outcome switch
            {
                ActivationReviewDispatchOutcome.Accepted => OutboxRequiredConsumerResult.Accepted(),
                ActivationReviewDispatchOutcome.Duplicate => OutboxRequiredConsumerResult.Duplicate(),
                _ => OutboxRequiredConsumerResult.Conflict(DescriptorActivationDiagnosticCodes.ReviewConflict.RequireValue(), "The activation review decision conflicts with the durable request state.")
            };
        }
        catch (InvalidOperationException exception)
        {
            // A persisted review fact or its HumanTask authority is malformed.
            // Retrying cannot repair it; keep the outbox fail-closed and let the
            // durable conflict/dead-letter path retain the evidence.
            return OutboxRequiredConsumerResult.Conflict(
                DescriptorActivationDiagnosticCodes.ReviewPayloadInvalid.RequireValue(),
                exception.Message);
        }
        catch (RuntimeStateContractException exception)
        {
            return OutboxRequiredConsumerResult.Conflict(
                DescriptorActivationDiagnosticCodes.ReviewPayloadInvalid.RequireValue(),
                exception.Message);
        }
    }

    private async Task<DescriptorActivationReviewDecision> EnrichDecisionAsync(
        DescriptorActivationReviewDecision parsedDecision,
        HumanTaskCompletedEvent @event,
        CancellationToken cancellationToken)
    {
        var instance = await _humanTaskInstanceStore.GetAsync(@event.HumanTaskKey, cancellationToken);
        if (instance is null)
        {
            throw new InvalidOperationException(
                $"HumanTask '{@event.HumanTaskKey.InstanceId}' is unavailable for activation review binding.");
        }

        if (instance.Status != HumanTaskInstanceStatus.Completed)
            throw new InvalidOperationException(
                $"HumanTask '{@event.HumanTaskKey.InstanceId}' is not durably completed.");

        if (string.IsNullOrWhiteSpace(instance.CompletionEventId)
            || !string.Equals(instance.CompletionEventId, @event.EventId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"HumanTask '{@event.HumanTaskKey.InstanceId}' completion event identity does not match the durable task.");

        if (!Equals(instance.Key, @event.HumanTaskKey)
            || !Equals(instance.HumanTaskPin, @event.HumanTaskPin))
            throw new InvalidOperationException(
                $"HumanTask '{@event.HumanTaskKey.InstanceId}' key or descriptor pin does not match the durable task.");

        if (!string.Equals(instance.Outcome, @event.Outcome, StringComparison.OrdinalIgnoreCase)
            || instance.Output is null
            || @event.Result is null
            || !Equals(instance.Output, @event.Result))
            throw new InvalidOperationException(
                $"HumanTask '{@event.HumanTaskKey.InstanceId}' completion fact does not match the durable result.");

        var persistedDecision = _stateRegistry.Restore<DescriptorActivationReviewDecision>(instance.Output);
        if (!Equals(persistedDecision, parsedDecision))
            throw new InvalidOperationException(
                $"HumanTask '{@event.HumanTaskKey.InstanceId}' typed decision does not match the durable result.");

        if (instance.Input is null)
            throw new InvalidOperationException(
                $"HumanTask '{@event.HumanTaskKey.InstanceId}' has no activation review input.");

        var taskInput = _stateRegistry.Restore<DescriptorActivationReviewTaskInput>(instance.Input);
        if (string.IsNullOrWhiteSpace(taskInput.ActivationRequestId)
            || !string.Equals(parsedDecision.ActivationRequestId, taskInput.ActivationRequestId, StringComparison.Ordinal)
            || (!string.IsNullOrEmpty(parsedDecision.TenantId)
                && !string.Equals(parsedDecision.TenantId, taskInput.TenantId, StringComparison.Ordinal))
            || !string.Equals(instance.TenantId, taskInput.TenantId, StringComparison.Ordinal)
            || !string.Equals(@event.HumanTaskKey.TenantId, taskInput.TenantId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"HumanTask '{@event.HumanTaskKey.InstanceId}' activation request or tenant binding does not match the durable input.");

        var correlationId = parsedDecision.CorrelationId;
        if (string.IsNullOrEmpty(correlationId))
            correlationId = taskInput.CorrelationId ?? string.Empty;
        else if (!string.Equals(correlationId, taskInput.CorrelationId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"HumanTask '{@event.HumanTaskKey.InstanceId}' correlation binding does not match the durable input.");

        var canonicalDecision = @event.Outcome switch
        {
            "Approve" => DescriptorActivationReviewOutcome.Approved,
            "Reject" => DescriptorActivationReviewOutcome.Rejected,
            _ => throw new InvalidOperationException(
                $"HumanTask '{@event.HumanTaskKey.InstanceId}' has unsupported activation review outcome '{@event.Outcome}'.")
        };
        if (parsedDecision.Decision != canonicalDecision)
            throw new InvalidOperationException(
                $"HumanTask '{@event.HumanTaskKey.InstanceId}' outcome does not match the typed activation review decision.");

        if (string.IsNullOrWhiteSpace(@event.ActorId)
            || string.IsNullOrWhiteSpace(parsedDecision.ActorId)
            || !string.Equals(parsedDecision.ActorId, @event.ActorId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"HumanTask '{@event.HumanTaskKey.InstanceId}' actor does not match the typed activation review decision.");

        return parsedDecision with
        {
            TenantId = taskInput.TenantId,
            CorrelationId = correlationId
        };
    }
}
