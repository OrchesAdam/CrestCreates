using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;
using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DescriptorReviewReportInputSnapshot))]
[JsonContractExplicitRoot(typeof(DescriptorReviewReportInputSnapshot))]
public sealed partial class DescriptorReviewReportInputSnapshotJsonSerializerContext : JsonSerializerContext
{
}
