namespace CrestCreates.Workflow;

public sealed class WorkflowPostCommitNotificationOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

internal interface IWorkflowPostCommitNotificationBudget
{
    TimeSpan Timeout { get; }
    CancellationTokenSource CreateCancellationSource();
}

internal sealed class DefaultWorkflowPostCommitNotificationBudget : IWorkflowPostCommitNotificationBudget
{
    private readonly WorkflowPostCommitNotificationOptions _options;

    public DefaultWorkflowPostCommitNotificationBudget(WorkflowPostCommitNotificationOptions options)
    {
        _options = options;
    }

    public TimeSpan Timeout
    {
        get
        {
            if (_options.Timeout <= TimeSpan.Zero || _options.Timeout == System.Threading.Timeout.InfiniteTimeSpan)
                throw new InvalidOperationException("Workflow post-commit notification timeout must be finite and positive.");
            return _options.Timeout;
        }
    }

    public CancellationTokenSource CreateCancellationSource()
    {
        var source = new CancellationTokenSource();
        source.CancelAfter(Timeout);
        return source;
    }
}
