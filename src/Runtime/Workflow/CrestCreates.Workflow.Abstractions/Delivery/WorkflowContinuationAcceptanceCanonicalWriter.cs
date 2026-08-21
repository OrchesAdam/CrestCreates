using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Workflow.Abstractions.Delivery;

internal static class WorkflowContinuationAcceptanceCanonicalWriter
{
    internal static CanonicalHash Compute(WorkflowContinuationAcceptance acceptance)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(acceptance.TenantScope.TenantId);
            writer.WriteStringValue(acceptance.CompletionEventId);
            writer.WriteStringValue(acceptance.HumanTaskKey.TenantId);
            writer.WriteStringValue(acceptance.HumanTaskKey.InstanceId);
            writer.WriteStringValue(acceptance.WorkflowKey.TenantId);
            writer.WriteStringValue(acceptance.WorkflowKey.InstanceId);
            writer.WriteStringValue(acceptance.Outcome);
            if (acceptance.Result is null) writer.WriteNullValue();
            else { writer.WriteStringValue(acceptance.Result.TypeId); writer.WriteStringValue(acceptance.Result.SchemaRef?.ToString()); writer.WriteStringValue(acceptance.Result.JsonPayload); }
            writer.WriteNumberValue(acceptance.WorkflowFromRevision);
            writer.WriteNumberValue(acceptance.WorkflowToRevision);
            writer.WriteEndArray();
        }
        return new CanonicalHash
        {
            Value = Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant(),
            Algorithm = "SHA-256",
            AlgorithmVersion = "1",
            ArtifactKind = "RuntimeWorkflowContinuationAcceptance",
            DescriptorKind = "Runtime",
            Scope = "InternalFull",
            Purpose = "Integrity",
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "runtime-workflow-continuation-acceptance-v1"
        };
    }
}
