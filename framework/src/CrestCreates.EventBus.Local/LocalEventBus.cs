using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local;

public class LocalEventBus : DefaultLocalEventBus
{
    public LocalEventBus(ILocalEventDispatcher dispatcher)
        : base(dispatcher)
    {
    }
}
