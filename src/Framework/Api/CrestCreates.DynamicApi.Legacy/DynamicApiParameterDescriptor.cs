using System.Reflection;

namespace CrestCreates.DynamicApi;

public sealed class DynamicApiParameterDescriptor
{
    public string Name { get; init; } = string.Empty;

    public ParameterInfo? ParameterInfo { get; init; }

    public Type ParameterType { get; init; } = null!;

    public DynamicApiParameterSource Source { get; init; }

    public bool IsOptional { get; init; }
}
