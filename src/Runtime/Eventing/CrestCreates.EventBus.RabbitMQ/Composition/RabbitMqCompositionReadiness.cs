using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.RabbitMQ.Composition;

/// <summary>
/// Startup gate between the RabbitMQ composition service (event kernel check + binding freeze) and the
/// consumer. The consumer never performs broker I/O before this gate is signalled, so hosted-service
/// registration order cannot affect the startup sequence; a freeze failure faults the gate and fails
/// host startup deterministically.
/// </summary>
public sealed class RabbitMqCompositionReadiness
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitAsync(CancellationToken cancellationToken) => _ready.Task.WaitAsync(cancellationToken);

    public void SignalReady() => _ready.TrySetResult();

    public void Fail(Exception exception) => _ready.TrySetException(exception);
}
