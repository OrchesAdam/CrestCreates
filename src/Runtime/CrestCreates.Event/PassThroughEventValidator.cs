using CrestCreates.Event.Abstractions;

namespace CrestCreates.Event;

public sealed class PassThroughEventValidator : IEventValidator
{
    public void ValidateOrThrow(string eventName, object? payload) { }

    public ValidationResult Validate(string eventName, object? payload)
        => new(true, EventValidationError.None, null);
}
