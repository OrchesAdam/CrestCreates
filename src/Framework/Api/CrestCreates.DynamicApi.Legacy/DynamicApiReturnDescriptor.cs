namespace CrestCreates.DynamicApi;

public sealed class DynamicApiReturnDescriptor
{
    public Type DeclaredType { get; init; } = null!;

    public Type? PayloadType { get; init; }

    public bool IsVoid { get; init; }
}
