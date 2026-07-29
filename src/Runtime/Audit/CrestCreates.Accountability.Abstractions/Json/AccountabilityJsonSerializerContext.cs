using System.Text.Json.Serialization;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Core.Abstractions.Serialization;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Accountability.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonContractSurface(typeof(Recording.IAuditRecorder))]
[JsonContractSurface(typeof(Sinks.IAuditSink))]
[JsonContractExplicitRoot(typeof(AuditEnvelope))]
[JsonContractExplicitRoot(typeof(AuditActor))]
[JsonContractExplicitRoot(typeof(AuditActorReference))]
[JsonContractExplicitRoot(typeof(AuditAction))]
[JsonContractExplicitRoot(typeof(AuditTarget))]
[JsonContractExplicitRoot(typeof(AuditOutcome))]
[JsonContractExplicitRoot(typeof(AuditRuntimeContext))]
[JsonContractExplicitRoot(typeof(AuditRuntimeReference))]
[JsonContractExplicitRoot(typeof(AuditDescriptorContext))]
[JsonContractExplicitRoot(typeof(AuditDescriptorReference))]
[JsonContractExplicitRoot(typeof(AuditDataSnapshot))]
[JsonContractExplicitRoot(typeof(AuditDataArtifact))]
[JsonContractExplicitRoot(typeof(AuditEvidenceReference))]
[JsonContractExplicitRoot(typeof(AuditPayload))]
[JsonContractExplicitRoot(typeof(AuditSanitizationStamp))]
[JsonContractExplicitRoot(typeof(Recording.AuditRecordResult))]
[JsonContractExplicitRoot(typeof(Recording.AuditRecordIssue))]
[JsonContractExplicitRoot(typeof(Sinks.AuditSinkWriteResult))]
[JsonContractExplicitRoot(typeof(Sinks.AuditSinkFailure))]
[JsonContractExplicitRoot(typeof(CanonicalHash))]
public sealed partial class AccountabilityJsonSerializerContext : JsonSerializerContext
{
}
