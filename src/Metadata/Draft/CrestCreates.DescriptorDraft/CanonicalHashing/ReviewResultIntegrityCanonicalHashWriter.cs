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
        public const string TenantId = nameof(ReviewResultIntegrityProjection.TenantId);
        public const string DraftId = nameof(ReviewResultIntegrityProjection.DraftId);
        public const string IsActivationEligible = nameof(ReviewResultIntegrityProjection.IsActivationEligible);
        public const string IsValid = nameof(ReviewResultIntegrityProjection.IsValid);
        public const string DiagnosticCount = nameof(ReviewResultIntegrityProjection.DiagnosticCount);
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
