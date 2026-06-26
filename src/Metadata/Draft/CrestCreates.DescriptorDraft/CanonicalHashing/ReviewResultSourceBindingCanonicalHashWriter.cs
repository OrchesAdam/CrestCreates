using System.Text.Json;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

namespace CrestCreates.DescriptorDraft.CanonicalHashing;

/// <summary>
/// Canonical JSON writer for ReviewResultSourceBindingProjection.
/// </summary>
public static class ReviewResultSourceBindingCanonicalHashWriter
{
    private static class Fields
    {
        public const string TenantId = "tenantId";
        public const string DraftId = "draftId";
        public const string IsActivationEligible = "isActivationEligible";
        public const string IsValid = "isValid";
        public const string Diagnostics = "diagnostics";
        public const string Code = "code";
        public const string Severity = "severity";
        public const string GovernanceDecision = "governanceDecision";
        public const string ImpactSeverity = "impactSeverity";
    }

    public static void WritePayload(Utf8JsonWriter writer, ReviewResultSourceBindingProjection projection)
    {
        writer.WriteStartObject();
        writer.WriteString(Fields.TenantId, projection.TenantId);
        writer.WriteString(Fields.DraftId, projection.DraftId);
        writer.WriteBoolean(Fields.IsActivationEligible, projection.IsActivationEligible);
        writer.WriteBoolean(Fields.IsValid, projection.IsValid);
        writer.WritePropertyName(Fields.Diagnostics);
        writer.WriteStartArray();
        foreach (var d in projection.Diagnostics.OrderBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Severity, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString(Fields.Code, d.Code);
            writer.WriteString(Fields.Severity, d.Severity);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        WriteStringOrNull(writer, Fields.GovernanceDecision, projection.GovernanceDecision);
        WriteStringOrNull(writer, Fields.ImpactSeverity, projection.ImpactSeverity);
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
