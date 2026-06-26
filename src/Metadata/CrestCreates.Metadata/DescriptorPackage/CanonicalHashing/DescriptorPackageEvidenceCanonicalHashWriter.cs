using System.Globalization;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;

namespace CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;

/// <summary>
/// Canonical JSON writer for DescriptorPackageEvidence.
/// Property order: topology, impact, compatibility, lifecycle, normalizedFindings.
/// Collections sorted by key using StringComparer.Ordinal.
/// </summary>
public static class DescriptorPackageEvidenceCanonicalHashWriter
{
    private static class Fields
    {
        public const string TopologyNodeCount = "topologyNodeCount";
        public const string TopologyEdgeCount = "topologyEdgeCount";
        public const string HasTopologyErrors = "hasTopologyErrors";
        public const string TopologyDiagnosticCounts = "topologyDiagnosticCounts";
        public const string MaxImpactSeverity = "maxImpactSeverity";
        public const string AffectedDescriptorCount = "affectedDescriptorCount";
        public const string ImpactPathCount = "impactPathCount";
        public const string ImpactDiagnosticCounts = "impactDiagnosticCounts";
        public const string MaxCompatibilityLevel = "maxCompatibilityLevel";
        public const string BreakingFindingCount = "breakingFindingCount";
        public const string SecuritySensitiveFindingCount = "securitySensitiveFindingCount";
        public const string UnsupportedFindingCount = "unsupportedFindingCount";
        public const string MaxLifecycleDecision = "maxLifecycleDecision";
        public const string RequiresReview = "requiresReview";
        public const string IsBlocked = "isBlocked";
        public const string PackageFindingCount = "packageFindingCount";
        public const string NormalizedFindings = "normalizedFindings";
        public const string Severity = "severity";
        public const string Code = "code";
        public const string Source = "source";
        public const string Message = "message";
        public const string Subject = "subject";
        public const string RelatedRefs = "relatedRefs";
        public const string Namespace = "namespace";
        public const string Id = "id";
        public const string Version = "version";
        public const string Count = "count";
    }

    public static void WritePayload(Utf8JsonWriter writer, DescriptorPackageEvidence evidence)
    {
        writer.WriteStartObject();

        // Topology
        writer.WriteNumber(Fields.TopologyNodeCount, evidence.TopologyNodeCount);
        writer.WriteNumber(Fields.TopologyEdgeCount, evidence.TopologyEdgeCount);
        writer.WriteBoolean(Fields.HasTopologyErrors, evidence.HasTopologyErrors);
        writer.WritePropertyName(Fields.TopologyDiagnosticCounts);
        WriteFindingCounts(writer, evidence.TopologyDiagnosticCounts);

        // Impact
        writer.WriteString(Fields.MaxImpactSeverity, evidence.MaxImpactSeverity.ToString());
        writer.WriteNumber(Fields.AffectedDescriptorCount, evidence.AffectedDescriptorCount);
        writer.WriteNumber(Fields.ImpactPathCount, evidence.ImpactPathCount);
        writer.WritePropertyName(Fields.ImpactDiagnosticCounts);
        WriteFindingCounts(writer, evidence.ImpactDiagnosticCounts);

        // Compatibility
        writer.WriteString(Fields.MaxCompatibilityLevel, evidence.MaxCompatibilityLevel.ToString());
        writer.WriteNumber(Fields.BreakingFindingCount, evidence.BreakingFindingCount);
        writer.WriteNumber(Fields.SecuritySensitiveFindingCount, evidence.SecuritySensitiveFindingCount);
        writer.WriteNumber(Fields.UnsupportedFindingCount, evidence.UnsupportedFindingCount);

        // Lifecycle
        writer.WriteString(Fields.MaxLifecycleDecision, evidence.MaxLifecycleDecision.ToString());
        writer.WriteBoolean(Fields.RequiresReview, evidence.RequiresReview);
        writer.WriteBoolean(Fields.IsBlocked, evidence.IsBlocked);
        writer.WriteNumber(Fields.PackageFindingCount, evidence.PackageFindingCount);

        // Normalized findings
        writer.WritePropertyName(Fields.NormalizedFindings);
        writer.WriteStartArray();
        foreach (var f in evidence.NormalizedFindings
            .OrderBy(f => f.Severity, StringComparer.Ordinal)
            .ThenBy(f => f.Code, StringComparer.Ordinal)
            .ThenBy(f => f.Source, StringComparer.Ordinal)
            .ThenBy(f => f.Message, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString(Fields.Severity, f.Severity);
            writer.WriteString(Fields.Code, f.Code);
            writer.WriteString(Fields.Source, f.Source);
            writer.WriteString(Fields.Message, f.Message);

            // Subject
            if (f.Subject is null)
            {
                writer.WriteNull(Fields.Subject);
            }
            else
            {
                writer.WritePropertyName(Fields.Subject);
                WriteDescriptorRef(writer, f.Subject.Value);
            }

            // RelatedRefs
            writer.WritePropertyName(Fields.RelatedRefs);
            writer.WriteStartArray();
            foreach (var r in f.RelatedRefs
                .OrderBy(r => r.Namespace, StringComparer.Ordinal)
                .ThenBy(r => r.Id, StringComparer.Ordinal)
                .ThenBy(r => r.Version))
            {
                writer.WriteStartObject();
                writer.WriteString(Fields.Namespace, r.Namespace);
                writer.WriteString(Fields.Id, r.Id);
                if (r.Version is null)
                    writer.WriteNull(Fields.Version);
                else
                    writer.WriteNumber(Fields.Version, r.Version.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteFindingCounts(Utf8JsonWriter writer,
        System.Collections.Generic.IReadOnlyList<Abstractions.Evidence.EvidenceFindingCount> counts)
    {
        writer.WriteStartArray();
        foreach (var c in counts
            .OrderBy(c => c.Severity, StringComparer.Ordinal)
            .ThenBy(c => c.Code, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString(Fields.Severity, c.Severity);
            writer.WriteString(Fields.Code, c.Code);
            writer.WriteNumber(Fields.Count, c.Count);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteDescriptorRef(Utf8JsonWriter writer, DescriptorRef r)
    {
        writer.WriteStartObject();
        writer.WriteString(Fields.Namespace, r.Namespace);
        writer.WriteString(Fields.Id, r.Id);
        if (r.Version is null)
            writer.WriteNull(Fields.Version);
        else
            writer.WriteNumber(Fields.Version, r.Version.Value);
        writer.WriteEndObject();
    }
}
