using System.Text.Json;
using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

namespace CrestCreates.DescriptorDraft.CanonicalHashing;

/// <summary>
/// Canonical JSON writer for ReviewResultIntegrityProjection.
/// </summary>
public static class ReviewResultIntegrityCanonicalHashWriter
{
    private static class Fields
    {
        public const string TenantId = "tenantId";
        public const string DraftId = "draftId";
        public const string IsActivationEligible = "isActivationEligible";
        public const string IsValid = "isValid";
        public const string DiagnosticCount = "diagnosticCount";
    }

    public static void WritePayload(Utf8JsonWriter writer, ReviewResultIntegrityProjection projection)
    {
        writer.WriteStartObject();
        writer.WriteString(Fields.TenantId, projection.TenantId);
        writer.WriteString(Fields.DraftId, projection.DraftId);
        writer.WriteBoolean(Fields.IsActivationEligible, projection.IsActivationEligible);
        writer.WriteBoolean(Fields.IsValid, projection.IsValid);
        writer.WriteNumber(Fields.DiagnosticCount, projection.DiagnosticCount);
        writer.WriteEndObject();
    }
}
