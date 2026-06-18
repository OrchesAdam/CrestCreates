using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.Abstractions;

public interface ILocalEventDispatcher
{
    Task DispatchAsync(ILocalEvent @event, CancellationToken cancellationToken = default);

    Task DispatchAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent;
}
