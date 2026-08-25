using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;

namespace CrestCreates.Workflow.Accountability;

internal sealed class WorkflowAccountabilityObserver : IWorkflowLifecycleObserver
{
    private readonly IAuditRecorder _recorder;
    private readonly ITransactionalOutboxWriter? _outbox;
    private readonly WorkflowAccountabilityOutboxAppender? _appender;

    public WorkflowAccountabilityObserver(IAuditRecorder recorder, ITransactionalOutboxWriter? outbox = null, WorkflowAccountabilityOutboxAppender? appender = null)
    {
        _recorder = recorder;
        _outbox = outbox;
        _appender = appender;
    }

    public async ValueTask ObserveAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken cancellationToken = default)
    {
        if (_appender?.IsEnabled == true) return;
        await _recorder.RecordAsync(WorkflowAccountabilityEnvelopeFactory.Create(lifecycleEvent), CancellationToken.None).ConfigureAwait(false);
    }

}
