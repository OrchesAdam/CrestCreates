namespace CrestCreates.Event.Abstractions;

public enum EventValidationError
{
    None,
    NotRegistered,
    Deprecated,
    Removed,
    InvalidScope,
    InvalidPayload       // Phase 3
}
