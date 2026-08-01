namespace CrestCreates.Runtime.Persistence.Abstractions.State;

public interface IRuntimeStateContractRegistry
{
    RuntimeStateValue Capture(object? value);

    RuntimeStateValue Capture<T>(T value);

    void Validate(RuntimeStateValue value);

    object? Restore(RuntimeStateValue value);

    T Restore<T>(RuntimeStateValue value);
}
