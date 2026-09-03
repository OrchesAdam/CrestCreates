using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Workflow.Abstractions.Delivery;

internal static class WorkflowAbortReceiptCanonicalWriter
{
    internal static bool Matches(WorkflowAbortReceipt receipt)
        => receipt.Integrity is not null
            && receipt.Integrity == Compute(receipt);

    internal static CanonicalHash Compute(WorkflowAbortReceipt receipt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(receipt.Scope.TenantId);
            writer.WriteStringValue(receipt.AbortOperationId);
            writer.WriteStringValue(receipt.WorkflowKey.TenantId);
            writer.WriteStringValue(receipt.WorkflowKey.InstanceId);
            writer.WriteStringValue(receipt.HumanTaskKey.TenantId);
            writer.WriteStringValue(receipt.HumanTaskKey.InstanceId);
            writer.WriteNumberValue(receipt.WorkflowFromRevision);
            writer.WriteNumberValue(receipt.WorkflowToRevision);
            writer.WriteStringValue(receipt.WorkflowPin.Ref.Namespace);
            writer.WriteStringValue(receipt.WorkflowPin.Ref.Id);
            writer.WriteNumberValue(receipt.WorkflowPin.Ref.Version.GetValueOrDefault());
            writer.WriteStringValue(receipt.HumanTaskPin.Ref.Namespace);
            writer.WriteStringValue(receipt.HumanTaskPin.Ref.Id);
            writer.WriteNumberValue(receipt.HumanTaskPin.Ref.Version.GetValueOrDefault());
            writer.WriteStringValue(receipt.Reason);
            writer.WriteEndArray();
        }

        return new CanonicalHash
        {
            Value = Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant(),
            Algorithm = "SHA-256",
            AlgorithmVersion = "1",
            ArtifactKind = "RuntimeWorkflowAbortReceipt",
            DescriptorKind = "Runtime",
            Scope = "InternalFull",
            Purpose = "Integrity",
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "runtime-workflow-abort-receipt-v1"
        };
    }
}
