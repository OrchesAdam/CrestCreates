namespace CrestCreates.Core.Abstractions.Serialization;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class JsonContractExplicitRootAttribute : Attribute
{
    public JsonContractExplicitRootAttribute(Type rootType)
        => RootType = rootType;
    
    public Type RootType { get; }
}
