using System.Text.Json.Serialization;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Core.Abstractions.Serialization;

namespace CrestCreates.Agent.ControlPlane.Activation;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DescriptorActivationReviewTaskInput))]
[JsonContractExplicitRoot(typeof(DescriptorActivationReviewTaskInput))]
public sealed partial class DescriptorActivationRuntimeStateJsonSerializerContext : JsonSerializerContext
{
}
