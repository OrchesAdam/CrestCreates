namespace CrestCreates.Event.Abstractions;

public sealed record ValidationResult(
    bool IsValid,
    EventValidationError ErrorCode,
    IEventDescriptor? Descriptor);
