using System.Text.Json.Serialization;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Core.Abstractions.Serialization;
using CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DescriptorPackagePreview))]
[JsonSerializable(typeof(PackageEvidencePreview))]
[JsonContractExplicitRoot(typeof(DescriptorPackagePreview))]
[JsonContractExplicitRoot(typeof(PackageEvidencePreview))]
public sealed partial class AgentPackageArtifactProjectionJsonSerializerContext : JsonSerializerContext
{
}
