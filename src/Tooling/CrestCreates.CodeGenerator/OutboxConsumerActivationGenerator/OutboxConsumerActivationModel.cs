using System.Collections.Immutable;

namespace CrestCreates.CodeGenerator.OutboxConsumerActivationGenerator;

internal sealed class ConsumerActivationModel
{
    public string Namespace { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string FullyQualifiedName { get; init; } = "";
    public string Accessibility { get; init; } = "internal";
    public ImmutableArray<ConstructorParameterInfo> ConstructorParameters { get; init; } = ImmutableArray<ConstructorParameterInfo>.Empty;
    public string HintName { get; init; } = "";
    public bool HasZeroParameters { get; init; }
}

internal sealed class ConstructorParameterInfo
{
    public string TypeName { get; init; } = "";
    public string GlobalTypeName { get; init; } = "";
    public string ParameterName { get; init; } = "";
}
