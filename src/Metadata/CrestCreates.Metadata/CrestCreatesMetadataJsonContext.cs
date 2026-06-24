using System.Text.Json.Serialization;
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using PackageType = CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackage;

namespace CrestCreates.Metadata;

[JsonSerializable(typeof(PackageType))]
[JsonSerializable(typeof(DescriptorManifest))]
[JsonSerializable(typeof(DescriptorManifestEntry))]
[JsonSerializable(typeof(DescriptorSnapshot))]
[JsonSerializable(typeof(SnapshotEntry))]
[JsonSerializable(typeof(DescriptorPackageEvidence))]
[JsonSerializable(typeof(EvidenceFinding))]
[JsonSerializable(typeof(EvidenceFindingCount))]
[JsonSerializable(typeof(DescriptorPackageRelationshipEntry))]
[JsonSerializable(typeof(DescriptorPackageDiagnostic))]
[JsonSerializable(typeof(DescriptorPackageDiff))]
[JsonSerializable(typeof(DescriptorDiffEntry))]
[JsonSerializable(typeof(DescriptorStateChange))]
[JsonSerializable(typeof(DescriptorPackageMetadataChange))]
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
