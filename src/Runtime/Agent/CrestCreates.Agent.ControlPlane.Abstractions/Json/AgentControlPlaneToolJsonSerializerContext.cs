using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonContractSurface(
    typeof(IReadOnlyControlPlaneTools),
    ExcludedParameterTypes = new[] { typeof(AgentToolInvocationContext) })]
[JsonContractSurface(
    typeof(IMutationControlPlaneTools),
    ExcludedParameterTypes = new[] { typeof(AgentToolInvocationContext) })]
[JsonContractSurface(typeof(IAgentToolManifestProvider))]
public sealed partial class AgentControlPlaneToolJsonSerializerContext
    : JsonSerializerContext
{
}
