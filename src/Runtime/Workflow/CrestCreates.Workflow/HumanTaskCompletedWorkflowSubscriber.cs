using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class HumanTaskCompletedWorkflowSubscriber
    : ILocalEventHandler<HumanTaskCompletedEvent>
{
    private readonly IWorkflowContinuationService _continuationService;

    public HumanTaskCompletedWorkflowSubscriber(
        IWorkflowContinuationService continuationService)
    {
        _continuationService = continuationService;
    }

    public Task HandleAsync(HumanTaskCompletedEvent evt, CancellationToken ct)
    {
        return _continuationService.ContinueAsync(
            new WorkflowContinuationRequest
            {
                HumanTaskId = evt.HumanTaskInstanceId,
                Outcome = evt.Outcome,
                Result = evt.Result,
                CompletionEventId = evt.EventId,
                TriggerOperationId = evt.EventId
            }, ct);
    }
}
