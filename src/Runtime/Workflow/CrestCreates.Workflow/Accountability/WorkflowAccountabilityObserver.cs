using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;

namespace CrestCreates.Workflow.Accountability;

internal sealed class WorkflowAccountabilityObserver : IWorkflowLifecycleObserver
{
    private readonly IAuditRecorder _recorder;
    private readonly ITransactionalOutboxWriter? _outbox;

    public WorkflowAccountabilityObserver(IAuditRecorder recorder, ITransactionalOutboxWriter? outbox = null)
    {
        _recorder = recorder;
        _outbox = outbox;
    }

    public async ValueTask ObserveAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken cancellationToken = default)
    {
        if (_outbox is not null && string.Equals(lifecycleEvent.EventType, "workflow.resumed", StringComparison.Ordinal)) return;
        await _recorder.RecordAsync(WorkflowAccountabilityEnvelopeFactory.Create(lifecycleEvent), CancellationToken.None).ConfigureAwait(false);
    }

}
