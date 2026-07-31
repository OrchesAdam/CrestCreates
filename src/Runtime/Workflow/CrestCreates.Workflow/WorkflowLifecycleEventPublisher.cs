using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Workflow;

internal sealed class WorkflowLifecycleEventPublisher : IWorkflowLifecycleEventPublisher
{
    private readonly IReadOnlyList<IWorkflowLifecycleObserver> _observers;
    private readonly IWorkflowPostCommitNotificationBudget _budget;
    private readonly ILogger<WorkflowLifecycleEventPublisher> _logger;

    public WorkflowLifecycleEventPublisher(
        IEnumerable<IWorkflowLifecycleObserver> observers,
        IWorkflowPostCommitNotificationBudget budget,
        ILogger<WorkflowLifecycleEventPublisher> logger)
    {
        _observers = observers.ToArray();
        _budget = budget;
        _logger = logger;
    }

    public Task PublishAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken ct)
    {
        return PublishCoreAsync(lifecycleEvent, ct);
    }

    private async Task PublishCoreAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken ct)
    {
        if (_observers.Count == 0) return;
        using var timeout = _budget.CreateCancellationSource();
        var tasks = new List<Task>(_observers.Count);
        foreach (var observer in _observers)
        {
            try { tasks.Add(ObserveAsync(observer, lifecycleEvent, timeout.Token)); }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Workflow lifecycle observer failed synchronously for {EventType}", lifecycleEvent.EventType);
            }
        }
        try { await Task.WhenAll(tasks).WaitAsync(_budget.Timeout).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Workflow lifecycle notification budget elapsed for {EventType}", lifecycleEvent.EventType);
        }
        catch (TimeoutException)
        {
            timeout.Cancel();
            _logger.LogWarning("Workflow lifecycle notification budget elapsed for {EventType}", lifecycleEvent.EventType);
        }
    }

    private async Task ObserveAsync(IWorkflowLifecycleObserver observer, WorkflowLifecycleEvent lifecycleEvent, CancellationToken ct)
    {
        try { await observer.ObserveAsync(lifecycleEvent, ct).ConfigureAwait(false); }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Workflow lifecycle observer failed for {EventType}", lifecycleEvent.EventType);
        }
    }
}
