using System;
using System.Collections.Generic;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Shared typed dispatch validation. The execution protocol itself lives on
/// <see cref="LocalEventInvoker"/> (Prepare resolves the exact instance batch once; the prepared
/// invocation executes that same batch); this class only provides the required-handler validation
/// used by prepare and by legacy diagnostic surfaces.
/// </summary>
public static class LocalEventDispatch
{
    /// <summary>
    /// Validates that every expected handler type is satisfied by one of the prepared instances.
    /// Called during Prepare with the binding's merged expected-handler list so that typed
    /// expected-handler diagnostics apply uniformly after contributor merge.
    /// </summary>
    public static void ValidateRequiredHandlers<TEvent>(
        IReadOnlyList<Type>? expectedHandlerTypes,
        IReadOnlyList<ILocalEventHandler<TEvent>> handlerInstances)
        where TEvent : ILocalEvent
    {
        ArgumentNullException.ThrowIfNull(handlerInstances);

        if (expectedHandlerTypes is null || expectedHandlerTypes.Count == 0)
        {
            return;
        }

        foreach (var expectedHandlerType in expectedHandlerTypes)
        {
            var satisfied = false;
            foreach (var handler in handlerInstances)
            {
                if (handler is not null && expectedHandlerType.IsInstanceOfType(handler))
                {
                    satisfied = true;
                    break;
                }
            }

            if (!satisfied)
            {
                throw new LocalEventRequiredHandlerMissingException(typeof(TEvent), expectedHandlerType);
            }
        }
    }
}
