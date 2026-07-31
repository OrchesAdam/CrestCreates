namespace CrestCreates.Runtime.Persistence.Abstractions.State;

public interface IRuntimeStateContractRegistry
{
    RuntimeStateValue Capture(object? value);

    RuntimeStateValue Capture<T>(T value);

    object? Restore(RuntimeStateValue value);

    T Restore<T>(RuntimeStateValue value);
}
