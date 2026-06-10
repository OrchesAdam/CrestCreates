using System.Text.Json.Serialization;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata;

[JsonSerializable(typeof(DescriptorManifest))]
[JsonSerializable(typeof(DescriptorSnapshot))]
[JsonSerializable(typeof(SchemaDescriptor))]
[JsonSerializable(typeof(CapabilityDescriptor))]
[JsonSerializable(typeof(EventDescriptor))]
[JsonSerializable(typeof(FormDescriptor))]
[JsonSerializable(typeof(HumanTaskDescriptor))]
[JsonSerializable(typeof(WorkflowDescriptor))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class CrestCreatesMetadataJsonContext : JsonSerializerContext
{
}
