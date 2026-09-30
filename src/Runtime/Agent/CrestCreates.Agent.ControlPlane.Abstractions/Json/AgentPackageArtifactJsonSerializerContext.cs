using System.Text.Json.Serialization;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Core.Abstractions.Serialization;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentPackageArtifactEnvelope))]
[JsonSerializable(typeof(AgentEvidenceArtifactEnvelope))]
[JsonSerializable(typeof(AgentPackageArtifactOwner))]
[JsonContractExplicitRoot(typeof(AgentPackageArtifactEnvelope))]
[JsonContractExplicitRoot(typeof(AgentEvidenceArtifactEnvelope))]
public sealed partial class AgentPackageArtifactJsonSerializerContext : JsonSerializerContext
{
}
