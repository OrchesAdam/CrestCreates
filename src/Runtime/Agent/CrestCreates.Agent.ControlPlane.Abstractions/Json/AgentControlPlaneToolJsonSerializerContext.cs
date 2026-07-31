using System.Text.Json.Serialization;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Core.Abstractions.Serialization;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonContractSurface(
    typeof(IAgentControlPlaneToolService),
    ExcludedParameterTypes = new[] { typeof(AgentToolInvocationContext) })]
[JsonContractSurface(typeof(IAgentToolManifestProvider))]

// Direct serialization roots outside the marked tool surfaces.
[JsonContractExplicitRoot(typeof(DescriptorActivationReviewDecision))]
[JsonContractExplicitRoot(typeof(DescriptorActivationReviewTaskInput))]
[JsonContractExplicitRoot(typeof(CanonicalHash))]
public sealed partial class AgentControlPlaneToolJsonSerializerContext
    : JsonSerializerContext
{
}
