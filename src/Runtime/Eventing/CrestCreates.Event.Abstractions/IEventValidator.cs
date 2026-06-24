namespace CrestCreates.Event.Abstractions;

public interface IEventValidator
{
    void ValidateOrThrow(string eventName, object? payload);
    ValidationResult Validate(string eventName, object? payload);
}
