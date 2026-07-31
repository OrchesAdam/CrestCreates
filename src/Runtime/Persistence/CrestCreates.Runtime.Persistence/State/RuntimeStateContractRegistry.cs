using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.Runtime.Persistence.State;

public sealed class RuntimeStateContractRegistry : IRuntimeStateContractRegistry
{
    private readonly IReadOnlyDictionary<string, RuntimeStateRegistration> _byTypeId;
    private readonly IReadOnlyDictionary<Type, RuntimeStateRegistration> _byClrType;

    internal RuntimeStateContractRegistry(IEnumerable<RuntimeStateRegistration> registrations)
    {
        var entries = registrations.ToArray();
        _byTypeId = entries.ToDictionary(x => x.TypeId, StringComparer.Ordinal);
        _byClrType = entries.ToDictionary(x => x.ClrType);
    }

    public RuntimeStateValue Capture(object? value)
    {
        if (value is null)
            throw new RuntimeStateContractException("Untyped null Runtime state has no registered CLR contract.");

        if (!_byClrType.TryGetValue(value.GetType(), out var registration))
        {
            throw new RuntimeStateContractException(
                $"Runtime state CLR type '{value.GetType().FullName}' is not registered.");
        }

        return EnforceLimits(registration.CaptureObject(value));
    }

    public RuntimeStateValue Capture<T>(T value)
    {
        if (!_byClrType.TryGetValue(typeof(T), out var registration))
        {
            throw new RuntimeStateContractException(
                $"Runtime state CLR type '{typeof(T).FullName}' is not registered.");
        }

        return EnforceLimits(value is null
            ? registration.CaptureTypedNull()
            : registration.CaptureObject(value!));
    }

    public object? Restore(RuntimeStateValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!_byTypeId.TryGetValue(value.TypeId, out var registration))
        {
            throw new RuntimeStateContractException(
                $"Runtime state TypeId '{value.TypeId}' is not registered.");
        }

        if (registration.SchemaRef != value.SchemaRef)
            throw new RuntimeStateContractException(
                $"Runtime state SchemaRef does not match registration for TypeId '{value.TypeId}'.");

        return registration.RestoreObject(ValidatePayload(value.JsonPayload));
    }

    public T Restore<T>(RuntimeStateValue value)
    {
        var restored = Restore(value);
        if (restored is null)
            return default!;
        if (restored is not T typed)
            throw new RuntimeStateContractException(
                $"Runtime state TypeId '{value.TypeId}' restored '{restored.GetType().FullName}', not '{typeof(T).FullName}'.");
        return typed;
    }

    private static RuntimeStateValue EnforceLimits(RuntimeStateValue value)
    {
        if (value.TypeId.Length > RuntimeStateLimits.MaxTypeIdLength)
            throw new RuntimeStateContractException("Runtime state TypeId exceeds the configured limit.");
        if (value.JsonPayload.Length > RuntimeStateLimits.MaxJsonPayloadCharacters)
            throw new RuntimeStateContractException("Runtime state JSON payload exceeds the configured limit.");
        return value;
    }

    private static string ValidatePayload(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length > RuntimeStateLimits.MaxJsonPayloadCharacters)
            throw new RuntimeStateContractException("Runtime state JSON payload exceeds the configured limit.");
        return payload;
    }
}
