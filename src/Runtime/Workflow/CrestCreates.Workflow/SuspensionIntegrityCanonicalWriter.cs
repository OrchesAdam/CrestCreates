using System.Text.Json;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

/// <summary>
/// Structured canonical writer for suspension receipt integrity hashing.
/// Each field is written as a named JSON property with explicit length-prefixed encoding
/// to prevent delimiter-bearing field values from producing structural encoding collisions.
/// </summary>
internal static class SuspensionIntegrityCanonicalWriter
{
    public static void Write(
        Utf8JsonWriter writer,
        RuntimeTenantScope scope,
        string operationId,
        WorkflowInstance before,
        WorkflowInstance after,
        HumanTaskInstance task)
    {
        writer.WriteStartObject();

        WriteStringField(writer, "tenantId", scope.TenantId ?? "<host>");
        WriteStringField(writer, "operationId", operationId);

        writer.WritePropertyName("workflowKey");
        WriteInstanceKey(writer, before.Key);

        writer.WritePropertyName("humanTaskKey");
        WriteInstanceKey(writer, task.Key);

        writer.WriteNumber("workflowFromRevision", before.Revision);
        writer.WriteNumber("workflowToRevision", before.Revision + 1);

        writer.WritePropertyName("workflowPin");
        WriteStructuredPin(writer, after.WorkflowPin);

        writer.WritePropertyName("humanTaskPin");
        WriteStructuredPin(writer, task.HumanTaskPin);

        writer.WritePropertyName("workflowVariables");
        WriteOrderedState(writer, after.Variables);

        writer.WritePropertyName("workflowStepVariables");
        WriteOrderedState(writer, after.StepVariables);

        writer.WritePropertyName("taskInput");
        WriteStateValue(writer, task.Input);

        writer.WriteEndObject();
    }

    private static void WriteStringField(Utf8JsonWriter writer, string name, string value)
    {
        writer.WriteString(name, value);
    }

    private static void WriteInstanceKey(Utf8JsonWriter writer, RuntimeInstanceKey key)
    {
        writer.WriteStartObject();
        WriteStringField(writer, "tenantId", key.TenantId ?? "<host>");
        WriteStringField(writer, "instanceId", key.InstanceId);
        writer.WriteEndObject();
    }

    private static void WriteStructuredPin(Utf8JsonWriter writer, RuntimeDescriptorPin pin)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("ref");
        writer.WriteStartObject();
        WriteStringField(writer, "namespace", pin.Ref.Namespace);
        WriteStringField(writer, "id", pin.Ref.Id);
        if (pin.Ref.Version.HasValue)
            writer.WriteNumber("version", pin.Ref.Version.Value);
        else
            writer.WriteNull("version");
        writer.WriteEndObject();

        writer.WritePropertyName("contractHash");
        WriteStructuredCanonicalHash(writer, pin.ContractHash);

        writer.WritePropertyName("definitionHash");
        WriteStructuredCanonicalHash(writer, pin.DefinitionHash);

        WriteStringField(writer, "snapshotId", pin.SnapshotId ?? "");

        writer.WriteEndObject();
    }

    private static void WriteStructuredCanonicalHash(Utf8JsonWriter writer, CanonicalHash hash)
    {
        writer.WriteStartObject();
        WriteStringField(writer, "value", hash.Value);
        WriteStringField(writer, "algorithm", hash.Algorithm);
        WriteStringField(writer, "algorithmVersion", hash.AlgorithmVersion);
        WriteStringField(writer, "artifactKind", hash.ArtifactKind);
        WriteStringField(writer, "descriptorKind", hash.DescriptorKind ?? "");
        WriteStringField(writer, "scope", hash.Scope);
        WriteStringField(writer, "purpose", hash.Purpose);
        WriteStringField(writer, "contractVersion", hash.ContractVersion);
        WriteStringField(writer, "canonicalShapeVersion", hash.CanonicalShapeVersion);
        writer.WriteEndObject();
    }

    private static void WriteOrderedState(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, RuntimeStateValue> values)
    {
        writer.WriteStartArray();
        foreach (var entry in values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            WriteStringField(writer, "key", entry.Key);
            writer.WritePropertyName("value");
            WriteStateValue(writer, entry.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteStateValue(Utf8JsonWriter writer, RuntimeStateValue? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        WriteStringField(writer, "typeId", value.TypeId);

        writer.WritePropertyName("schemaRef");
        if (value.SchemaRef.HasValue)
        {
            var schemaRef = value.SchemaRef.Value;
            writer.WriteStartObject();
            WriteStringField(writer, "namespace", schemaRef.Namespace);
            WriteStringField(writer, "id", schemaRef.Id);
            if (schemaRef.Version.HasValue)
                writer.WriteNumber("version", schemaRef.Version.Value);
            else
                writer.WriteNull("version");
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNullValue();
        }

        WriteStringField(writer, "jsonPayload", value.JsonPayload ?? "");

        writer.WriteEndObject();
    }
}
