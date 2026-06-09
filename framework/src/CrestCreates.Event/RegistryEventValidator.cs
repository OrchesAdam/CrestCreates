using CrestCreates.Event.Abstractions;

namespace CrestCreates.Event;

public sealed class RegistryEventValidator : IEventValidator
{
    private readonly IEventResolver _resolver;
    private readonly IEventMetadataProvider _metadata;

    public RegistryEventValidator(IEventResolver resolver, IEventMetadataProvider metadata)
    {
        _resolver = resolver;
        _metadata = metadata;
    }

    public void ValidateOrThrow(string eventName, object? payload)
    {
        if (_metadata.State != RegistryState.Built)
            throw new InvalidOperationException(
                "EventRegistry has not been built yet. Publish cannot occur before Build completes.");

        var active = _resolver.GetByName(eventName);
        if (active is not null) return;

        var latest = _metadata.GetLatestVersion(eventName);
        if (latest is null)
            throw new EventValidationException(
                $"Event '{eventName}' is not registered. " +
                "Apply [CrestEvent] to the event class or register via IDynamicEventRegistry.");

        if (latest.State == Metadata.Abstractions.DescriptorState.Deprecated)
            throw new EventValidationException(
                $"Event '{eventName}' is deprecated. All versions are deprecated.");

        if (latest.State == Metadata.Abstractions.DescriptorState.Removed)
            throw new EventValidationException(
                $"Event '{eventName}' has been removed.");
    }

    public ValidationResult Validate(string eventName, object? payload)
    {
        try
        {
            ValidateOrThrow(eventName, payload);
            return new ValidationResult(true, EventValidationError.None, _resolver.GetByName(eventName));
        }
        catch (EventValidationException ex)
        {
            var errorCode = ex.Message.Contains("deprecated", StringComparison.OrdinalIgnoreCase)
                ? EventValidationError.Deprecated
                : ex.Message.Contains("removed", StringComparison.OrdinalIgnoreCase)
                    ? EventValidationError.Removed
                    : EventValidationError.NotRegistered;
            return new ValidationResult(false, errorCode, null);
        }
        catch (InvalidOperationException)
        {
            return new ValidationResult(false, EventValidationError.NotRegistered, null);
        }
    }
}

public sealed class EventValidationException : Exception
{
    public EventValidationException(string message) : base(message) { }
}
