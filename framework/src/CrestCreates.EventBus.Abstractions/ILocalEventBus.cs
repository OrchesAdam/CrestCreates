using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.Abstractions;

public interface ILocalEventBus
{
    Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default);

    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent;
}
