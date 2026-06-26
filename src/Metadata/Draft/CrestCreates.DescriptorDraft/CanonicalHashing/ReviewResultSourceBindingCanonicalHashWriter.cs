using System.Text.Json;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

namespace CrestCreates.DescriptorDraft.CanonicalHashing;

/// <summary>
/// Canonical JSON writer for ReviewResultSourceBindingProjection.
/// </summary>
public static class ReviewResultSourceBindingCanonicalHashWriter
{
    public static void WritePayload(Utf8JsonWriter writer, ReviewResultSourceBindingProjection projection)
    {
        writer.WriteStartObject();
        writer.WriteString("tenantId", projection.TenantId);
        writer.WriteString("draftId", projection.DraftId);
        writer.WriteBoolean("isActivationEligible", projection.IsActivationEligible);
        writer.WriteBoolean("isValid", projection.IsValid);
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (var d in projection.Diagnostics.OrderBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Severity, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("code", d.Code);
            writer.WriteString("severity", d.Severity);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        WriteStringOrNull(writer, "governanceDecision", projection.GovernanceDecision);
        WriteStringOrNull(writer, "impactSeverity", projection.ImpactSeverity);
        writer.WriteEndObject();
    }

    private static void WriteStringOrNull(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
            writer.WriteNull(propertyName);
        else
            writer.WriteString(propertyName, value);
    }
}
