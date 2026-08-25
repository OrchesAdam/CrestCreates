using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class WorkflowContinuationOutboxConsumer : IOutboxRequiredConsumer<HumanTaskCompletedEvent>
{
    private readonly IWorkflowContinuationService _continuation;
    public WorkflowContinuationOutboxConsumer(IWorkflowContinuationService continuation) => _continuation = continuation;
    public string ConsumerId => HumanTaskDeliveryConstants.WorkflowContinuationConsumerId;

    public async ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(HumanTaskCompletedEvent payload, OutboxDeliveryContext context, CancellationToken cancellationToken = default)
    {
        if (payload.WorkflowKey is null)
            return OutboxRequiredConsumerResult.Conflict(
                "WORKFLOW_CONTINUATION_CORRELATION_MISSING",
                "A required workflow continuation completion must carry its persisted WorkflowKey.");
        try
        {
            await _continuation.ContinueAsync(new WorkflowContinuationRequest
            {
                HumanTaskKey = payload.HumanTaskKey,
                WorkflowKey = payload.WorkflowKey.Value,
                Outcome = payload.Outcome,
                Result = payload.Result,
                CompletionEventId = payload.EventId,
                TriggerOperationId = payload.EventId
            }, cancellationToken).ConfigureAwait(false);
            return OutboxRequiredConsumerResult.Accepted();
        }
        catch (RuntimeConcurrencyException ex)
        {
            return OutboxRequiredConsumerResult.Retry("WORKFLOW_CONCURRENCY", ex.Message);
        }
        catch (RuntimePersistenceContractException ex)
        {
            return OutboxRequiredConsumerResult.Conflict("WORKFLOW_CONTINUATION_CONFLICT", ex.Message);
        }
        catch (Exception ex)
        {
            return OutboxRequiredConsumerResult.Retry("WORKFLOW_CONTINUATION_FAILED", ex.Message);
        }
    }
}
