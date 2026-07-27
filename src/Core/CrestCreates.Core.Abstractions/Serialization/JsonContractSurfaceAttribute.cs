namespace CrestCreates.Core.Abstractions.Serialization;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class JsonContractSurfaceAttribute : Attribute
{
    public JsonContractSurfaceAttribute(Type surfaceType)
        => SurfaceType = surfaceType;

    public Type SurfaceType { get; }

    public Type[] ExcludedParameterTypes { get; set; } = [];
}
