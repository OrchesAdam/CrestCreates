namespace CrestCreates.Runtime.Delivery.Bootstrap;

internal sealed class OutboxCompositionReadiness
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _failure;

    public bool IsReady => _ready.Task.IsCompletedSuccessfully && _failure is null;
    public Exception? Failure => _failure;

    public void Open() => _ready.TrySetResult();

    public void Fail(Exception exception)
    {
        _failure = exception;
        _ready.TrySetException(exception);
    }

    public Task WaitAsync(CancellationToken cancellationToken)
        => _failure is null ? _ready.Task.WaitAsync(cancellationToken) : Task.FromException(_failure);
}
