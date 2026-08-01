using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.Runtime.Persistence.State;

internal abstract class RuntimeStateRegistration
{
    protected RuntimeStateRegistration(string typeId, Type clrType, DescriptorRef? schemaRef)
    {
        TypeId = typeId;
        ClrType = clrType;
        SchemaRef = schemaRef;
    }

    public string TypeId { get; }

    public Type ClrType { get; }

    public DescriptorRef? SchemaRef { get; }

    public abstract RuntimeStateValue CaptureTypedNull();

    public abstract RuntimeStateValue CaptureObject(object value);

    public abstract object? RestoreObject(string jsonPayload);
}

internal sealed class TypedRuntimeStateRegistration<T> : RuntimeStateRegistration
{
    private readonly JsonTypeInfo<T> _jsonTypeInfo;

    public TypedRuntimeStateRegistration(
        string typeId,
        JsonTypeInfo<T> jsonTypeInfo,
        DescriptorRef? schemaRef)
        : base(typeId, typeof(T), schemaRef)
    {
        _jsonTypeInfo = jsonTypeInfo ?? throw new ArgumentNullException(nameof(jsonTypeInfo));
    }

    public override RuntimeStateValue CaptureTypedNull()
        => new()
        {
            TypeId = TypeId,
            SchemaRef = SchemaRef,
            JsonPayload = JsonSerializer.Serialize<T>(default!, _jsonTypeInfo)
        };

    public override RuntimeStateValue CaptureObject(object value)
    {
        if (value is not T typedValue)
        {
            throw new RuntimeStateContractException(
                $"Runtime state value type '{value.GetType().FullName}' does not match registered type '{typeof(T).FullName}'.");
        }

        return new RuntimeStateValue
        {
            TypeId = TypeId,
            SchemaRef = SchemaRef,
            JsonPayload = JsonSerializer.Serialize(typedValue, _jsonTypeInfo)
        };
    }

    public override object? RestoreObject(string jsonPayload)
        => JsonSerializer.Deserialize(jsonPayload, _jsonTypeInfo);
}
