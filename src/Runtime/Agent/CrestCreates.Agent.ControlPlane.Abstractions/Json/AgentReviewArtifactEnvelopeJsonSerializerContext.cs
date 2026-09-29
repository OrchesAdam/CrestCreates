using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentReviewArtifactEnvelope))]
[JsonContractExplicitRoot(typeof(AgentReviewArtifactEnvelope))]
public sealed partial class AgentReviewArtifactEnvelopeJsonSerializerContext : JsonSerializerContext
{
}
