using System.Buffers;
using System.Collections.Immutable;
using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Accountability.CanonicalHashing;

/// <summary>
/// The single hand-written safe-envelope projection used for both byte limits and hashing.
/// </summary>
public sealed class AccountabilityCanonicalProjectionWriter
{
    public const string AlgorithmVersion = "sha256-canonical-json-v1";
    public const string ContractVersion = "canonical-hash-v1";
    public const string CanonicalShapeVersion = "accountability-record-hash-v1";

    public CanonicalHashProjectionResult CreateProjection(AuditEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var metadata = new CanonicalHashMetadata
        {
            ArtifactKind = CanonicalHashArtifactNames.AccountabilityRecord,
            Purpose = CanonicalHashPurposeNames.AuditEvidence,
            Scope = "InternalFull",
            AlgorithmVersion = AlgorithmVersion,
            ContractVersion = ContractVersion,
            CanonicalShapeVersion = CanonicalShapeVersion
        };
        return CanonicalHashProjectionResult.Create(metadata, writer => Write(envelope, writer));
    }

    public int MeasureBytes(AuditEnvelope envelope)
    {
        var buffer = new CountingBufferWriter();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = true });
        Write(envelope, writer);
        writer.Flush();
        return buffer.Count;
    }

    public void Write(AuditEnvelope envelope, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteNumber("contractVersion", envelope.ContractVersion);
        writer.WriteString("auditId", envelope.AuditId);
        writer.WriteString("occurredAt", envelope.OccurredAt);
        WriteNullableString(writer, "tenantId", envelope.TenantId);
        writer.WriteString("correlationId", envelope.CorrelationId);
        WriteNullableString(writer, "causationId", envelope.CausationId);
        WriteNullableString(writer, "parentAuditId", envelope.ParentAuditId);
        WriteNullableString(writer, "previousAuditId", envelope.PreviousAuditId);
        WriteActor(writer, envelope.Actor);
        WriteAction(writer, envelope.Action);
        WriteTarget(writer, envelope.Target);
        WriteOutcome(writer, envelope.Outcome);
        WriteRuntime(writer, envelope.Runtime);
        WriteDescriptors(writer, envelope.Descriptors);
        WriteDataSnapshot(writer, envelope.DataSnapshot);
        WriteEvidence(writer, envelope.Evidence);
        WritePayload(writer, envelope.Payload);
        WriteTags(writer, envelope.Tags);
        WriteSanitization(writer, envelope.Sanitization);
        writer.WriteEndObject();
    }

    private static void WriteActor(Utf8JsonWriter writer, AuditActor actor)
    {
        writer.WritePropertyName("actor");
        writer.WriteStartObject();
        writer.WriteString("kind", actor.Kind);
        writer.WriteString("id", actor.Id);
        WriteNullableString(writer, "displayName", actor.DisplayName);
        WriteActorRef(writer, "initiatedBy", actor.InitiatedBy);
        WriteActorRef(writer, "onBehalfOf", actor.OnBehalfOf);
        WriteNullableString(writer, "delegationId", actor.DelegationId);
        WriteNullableString(writer, "impersonationId", actor.ImpersonationId);
        writer.WriteEndObject();
    }

    private static void WriteActorRef(Utf8JsonWriter writer, string name, AuditActorReference? value)
    {
        writer.WritePropertyName(name);
        if (value is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind);
        writer.WriteString("id", value.Id);
        writer.WriteEndObject();
    }

    private static void WriteAction(Utf8JsonWriter writer, AuditAction action)
    {
        writer.WritePropertyName("action");
        writer.WriteStartObject();
        writer.WriteString("kind", action.Kind);
        writer.WriteString("name", action.Name);
        writer.WriteEndObject();
    }

    private static void WriteTarget(Utf8JsonWriter writer, AuditTarget target)
    {
        writer.WritePropertyName("target");
        writer.WriteStartObject();
        writer.WriteString("kind", target.Kind);
        writer.WriteString("id", target.Id);
        WriteNullableString(writer, "version", target.Version);
        writer.WriteEndObject();
    }

    private static void WriteOutcome(Utf8JsonWriter writer, AuditOutcome outcome)
    {
        writer.WritePropertyName("outcome");
        writer.WriteStartObject();
        writer.WriteString("status", outcome.Status);
        WriteNullableString(writer, "code", outcome.Code);
        WriteNullableString(writer, "safeSummary", outcome.SafeSummary);
        writer.WriteEndObject();
    }

    private static void WriteRuntime(Utf8JsonWriter writer, AuditRuntimeContext runtime)
    {
        writer.WritePropertyName("runtime");
        writer.WriteStartObject();
        WriteNullableString(writer, "invocationSource", runtime.InvocationSource);
        WriteNullableString(writer, "executionId", runtime.ExecutionId);
        WriteNullableString(writer, "requestId", runtime.RequestId);
        WriteNullableString(writer, "traceId", runtime.TraceId);
        WriteNullableString(writer, "spanId", runtime.SpanId);
        if (runtime.Duration is { } duration) writer.WriteString("duration", duration.ToString("c", System.Globalization.CultureInfo.InvariantCulture));
        else writer.WriteNull("duration");
        WriteRuntimeReferences(writer, runtime.References);
        writer.WriteEndObject();
    }

    private static void WriteRuntimeReferences(Utf8JsonWriter writer, ImmutableArray<AuditRuntimeReference> references)
    {
        writer.WritePropertyName("references");
        writer.WriteStartArray();
        foreach (var reference in references.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", reference.Kind);
            writer.WriteString("id", reference.Id);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteDescriptors(Utf8JsonWriter writer, AuditDescriptorContext descriptors)
    {
        writer.WritePropertyName("descriptors");
        writer.WriteStartObject();
        WriteNullableString(writer, "snapshotId", descriptors.SnapshotId);
        WriteHash(writer, "snapshotHash", descriptors.SnapshotHash);
        writer.WritePropertyName("items");
        writer.WriteStartArray();
        foreach (var item in descriptors.Items.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ThenBy(x => x.Version))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", item.Kind);
            writer.WriteString("id", item.Id);
            writer.WriteNumber("version", item.Version);
            WriteHash(writer, "contractHash", item.ContractHash);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteDataSnapshot(Utf8JsonWriter writer, AuditDataSnapshot? snapshot)
    {
        writer.WritePropertyName("dataSnapshot");
        if (snapshot is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteString("capturePolicyId", snapshot.CapturePolicyId);
        writer.WriteNumber("capturePolicyVersion", snapshot.CapturePolicyVersion);
        writer.WritePropertyName("artifacts");
        writer.WriteStartArray();
        foreach (var artifact in snapshot.Artifacts.OrderBy(x => x.Kind, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", artifact.Kind);
            WriteHash(writer, "contentHash", artifact.ContentHash);
            if (artifact.ContentHashBasis is { } basis)
                writer.WriteString("hashBasis", basis == AuditDataHashBasis.Source ? "source" : "sanitized");
            else writer.WriteNull("hashBasis");
            writer.WritePropertyName("sanitizedValue");
            if (artifact.SanitizedValue is { } value) WriteCanonicalJsonElement(writer, value); else writer.WriteNullValue();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteEvidence(Utf8JsonWriter writer, ImmutableArray<AuditEvidenceReference> evidence)
    {
        writer.WritePropertyName("evidence");
        writer.WriteStartArray();
        foreach (var item in evidence.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", item.Kind);
            writer.WriteString("id", item.Id);
            WriteHash(writer, "hash", item.Hash);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WritePayload(Utf8JsonWriter writer, AuditPayload? payload)
    {
        writer.WritePropertyName("payload");
        if (payload is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteString("kind", payload.Kind);
        writer.WriteNumber("version", payload.Version);
        writer.WritePropertyName("data");
        WriteCanonicalJsonElement(writer, payload.Data);
        writer.WriteEndObject();
    }

    private static void WriteCanonicalJsonElement(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJsonElement(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonicalJsonElement(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static void WriteTags(Utf8JsonWriter writer, ImmutableSortedDictionary<string, string>? tags)
    {
        writer.WritePropertyName("tags");
        writer.WriteStartObject();
        if (tags is not null)
            foreach (var pair in tags.OrderBy(x => x.Key, StringComparer.Ordinal)) writer.WriteString(pair.Key, pair.Value);
        writer.WriteEndObject();
    }

    private static void WriteSanitization(Utf8JsonWriter writer, AuditSanitizationStamp? stamp)
    {
        writer.WritePropertyName("sanitization");
        if (stamp is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteString("policyId", stamp.PolicyId);
        writer.WriteNumber("policyVersion", stamp.PolicyVersion);
        writer.WritePropertyName("appliedRuleIds");
        writer.WriteStartArray();
        foreach (var id in stamp.AppliedRuleIds.OrderBy(x => x, StringComparer.Ordinal)) writer.WriteStringValue(id);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteHash(Utf8JsonWriter writer, string name, CanonicalHash? hash)
    {
        writer.WritePropertyName(name);
        if (hash is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteString("value", hash.Value);
        writer.WriteString("algorithm", hash.Algorithm);
        writer.WriteString("algorithmVersion", hash.AlgorithmVersion);
        writer.WriteString("artifactKind", hash.ArtifactKind);
        WriteNullableString(writer, "descriptorKind", hash.DescriptorKind);
        writer.WriteString("scope", hash.Scope);
        writer.WriteString("purpose", hash.Purpose);
        writer.WriteString("contractVersion", hash.ContractVersion);
        writer.WriteString("canonicalShapeVersion", hash.CanonicalShapeVersion);
        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name); else writer.WriteString(name, value);
    }

    private sealed class CountingBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer = new byte[256];
        public int Count { get; private set; }
        public void Advance(int count)
        {
            if (count < 0 || Count + count > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            Count += count;
        }
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer.AsMemory(Count);
        }
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer.AsSpan(Count);
        }
        private void Ensure(int sizeHint)
        {
            if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
            var required = checked(Count + Math.Max(sizeHint, 1));
            if (required <= _buffer.Length) return;
            Array.Resize(ref _buffer, Math.Max(required, _buffer.Length * 2));
        }
    }
}
