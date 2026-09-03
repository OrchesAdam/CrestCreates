using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;

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
            WritePin(writer, receipt.WorkflowPin);
            WritePin(writer, receipt.HumanTaskPin);
            writer.WriteStringValue(receipt.Reason);
            writer.WriteStringValue(receipt.AcceptedAt);
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
            CanonicalShapeVersion = "runtime-workflow-abort-receipt-v2"
        };
    }

    private static void WritePin(Utf8JsonWriter writer, RuntimeDescriptorPin pin)
    {
        writer.WriteStartObject();
        writer.WriteStartObject("ref");
        writer.WriteString("namespace", pin.Ref.Namespace);
        writer.WriteString("id", pin.Ref.Id);
        if (pin.Ref.Version.HasValue)
            writer.WriteNumber("version", pin.Ref.Version.Value);
        else
            writer.WriteNull("version");
        writer.WriteEndObject();
        WriteHash(writer, "contractHash", pin.ContractHash);
        WriteHash(writer, "definitionHash", pin.DefinitionHash);
        writer.WriteString("snapshotId", pin.SnapshotId ?? string.Empty);
        writer.WriteEndObject();
    }

    private static void WriteHash(Utf8JsonWriter writer, string name, CanonicalHash hash)
    {
        writer.WriteStartObject(name);
        writer.WriteString("value", hash.Value);
        writer.WriteString("algorithm", hash.Algorithm);
        writer.WriteString("algorithmVersion", hash.AlgorithmVersion);
        writer.WriteString("artifactKind", hash.ArtifactKind);
        writer.WriteString("descriptorKind", hash.DescriptorKind ?? string.Empty);
        writer.WriteString("scope", hash.Scope);
        writer.WriteString("purpose", hash.Purpose);
        writer.WriteString("contractVersion", hash.ContractVersion);
        writer.WriteString("canonicalShapeVersion", hash.CanonicalShapeVersion);
        writer.WriteEndObject();
    }
}
