using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.Local;

public class LocalEventBus : DefaultLocalEventBus
{
    public LocalEventBus(
        ILocalEventDispatcher dispatcher,
        IEventValidator validator,
        IDeadLetterStore? deadLetterStore = null,
        IOptions<LocalDeadLetterOptions>? deadLetterOptions = null)
        : base(dispatcher, validator, deadLetterStore, deadLetterOptions)
    {
    }
}
