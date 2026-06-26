using System.Text.Json;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

namespace CrestCreates.DescriptorDraft.CanonicalHashing;

/// <summary>
/// Canonical JSON writer for ReviewResultIntegrityProjection.
/// </summary>
public static class ReviewResultIntegrityCanonicalHashWriter
{
    public static void WritePayload(Utf8JsonWriter writer, ReviewResultIntegrityProjection projection)
    {
        writer.WriteStartObject();
        writer.WriteString("tenantId", projection.TenantId);
        writer.WriteString("draftId", projection.DraftId);
        writer.WriteBoolean("isActivationEligible", projection.IsActivationEligible);
        writer.WriteBoolean("isValid", projection.IsValid);
        writer.WriteNumber("diagnosticCount", projection.DiagnosticCount);
        writer.WriteEndObject();
    }
}
