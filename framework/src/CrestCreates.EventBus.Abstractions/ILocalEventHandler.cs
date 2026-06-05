using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.Abstractions;

public interface ILocalEventHandler<in TEvent>
    where TEvent : ILocalEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
