namespace CrestCreates.Event.Abstractions;

public interface IEventValidator
{
    void ValidateOrThrow(string eventName, object? payload);
    ValidationResult Validate(string eventName, object? payload);
}

public sealed record ValidationResult(
    bool IsValid,
    EventValidationError ErrorCode,
    IEventDescriptor? Descriptor);

public enum EventValidationError
{
    None,
    NotRegistered,
    Deprecated,
    Removed,
    InvalidScope,
    InvalidPayload       // Phase 3
}
