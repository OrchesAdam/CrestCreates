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
        public const string TenantId = nameof(ReviewResultSourceBindingProjection.TenantId);
        public const string DraftId = nameof(ReviewResultSourceBindingProjection.DraftId);
        public const string IsActivationEligible = nameof(ReviewResultSourceBindingProjection.IsActivationEligible);
        public const string IsValid = nameof(ReviewResultSourceBindingProjection.IsValid);
        public const string Diagnostics = nameof(ReviewResultSourceBindingProjection.Diagnostics);
        public const string Code = nameof(ReviewDiagnosticProjection.Code);
        public const string Severity = nameof(ReviewDiagnosticProjection.Severity);
        public const string GovernanceDecision = nameof(ReviewResultSourceBindingProjection.GovernanceDecision);
        public const string ImpactSeverity = nameof(ReviewResultSourceBindingProjection.ImpactSeverity);
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
