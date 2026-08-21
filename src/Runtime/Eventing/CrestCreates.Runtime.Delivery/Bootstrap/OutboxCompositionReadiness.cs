namespace CrestCreates.Runtime.Delivery.Bootstrap;

internal sealed class OutboxCompositionReadiness
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsReady => _ready.Task.IsCompletedSuccessfully;

    public void Open() => _ready.TrySetResult();

    public Task WaitAsync(CancellationToken cancellationToken)
        => _ready.Task.WaitAsync(cancellationToken);
}
