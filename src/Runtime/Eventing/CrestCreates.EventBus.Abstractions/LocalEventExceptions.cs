using System;
using System.Collections.Generic;
using System.Linq;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>Thrown before any handler runs / before channel enqueue when an event type is not declared.</summary>
public sealed class UnknownLocalEventException : InvalidOperationException
{
    public UnknownLocalEventException(Type eventType)
        : base($"Local event '{eventType.FullName}' is not declared for typed dispatch. " +
               "Declare it via a generated contributor (handler implementation or [LocalEvent] on the type) " +
               "and enable that assembly with its generated AddLocalEvents() extension, or declare it explicitly " +
               "in AddCrestLocalEventDispatch. Undeclared events never dispatch and never succeed silently.")
    {
        EventType = eventType;
    }

    public Type EventType { get; }
}

/// <summary>Thrown before any handler runs when a generated handler binding is not satisfied by DI.</summary>
public sealed class LocalEventRequiredHandlerMissingException : InvalidOperationException
{
    public LocalEventRequiredHandlerMissingException(Type eventType, Type handlerType)
        : base($"Local event '{eventType.FullName}' declares required handler '{handlerType.FullName}' " +
               "but no instance assignable to it is registered in the current scope. The binding exists but its " +
               "DI registration is missing; this is a configuration failure, not a zero-handler notification.")
    {
        EventType = eventType;
        HandlerType = handlerType;
    }

    public Type EventType { get; }

    public Type HandlerType { get; }
}

/// <summary>Thrown at registry freeze when contributed registrations conflict or lack required capability.</summary>
public sealed class LocalEventDispatchConfigurationException : InvalidOperationException
{
    public LocalEventDispatchConfigurationException(string message, IReadOnlyList<string> violations)
        : base(message)
    {
        Violations = violations;
    }

    public IReadOnlyList<string> Violations { get; }

    public static LocalEventDispatchConfigurationException ForViolations(IReadOnlyList<string> violations)
        => new(
            "Local event dispatch registration is invalid:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(violation => "- " + violation)),
            violations);
}
