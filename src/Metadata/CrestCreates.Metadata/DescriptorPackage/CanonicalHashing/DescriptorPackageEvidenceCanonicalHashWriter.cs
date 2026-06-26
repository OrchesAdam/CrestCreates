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
    public static void WritePayload(Utf8JsonWriter writer, DescriptorPackageEvidence evidence)
    {
        writer.WriteStartObject();

        // Topology
        writer.WriteNumber("topologyNodeCount", evidence.TopologyNodeCount);
        writer.WriteNumber("topologyEdgeCount", evidence.TopologyEdgeCount);
        writer.WriteBoolean("hasTopologyErrors", evidence.HasTopologyErrors);
        writer.WritePropertyName("topologyDiagnosticCounts");
        WriteFindingCounts(writer, evidence.TopologyDiagnosticCounts);

        // Impact
        writer.WriteString("maxImpactSeverity", evidence.MaxImpactSeverity.ToString());
        writer.WriteNumber("affectedDescriptorCount", evidence.AffectedDescriptorCount);
        writer.WriteNumber("impactPathCount", evidence.ImpactPathCount);
        writer.WritePropertyName("impactDiagnosticCounts");
        WriteFindingCounts(writer, evidence.ImpactDiagnosticCounts);

        // Compatibility
        writer.WriteString("maxCompatibilityLevel", evidence.MaxCompatibilityLevel.ToString());
        writer.WriteNumber("breakingFindingCount", evidence.BreakingFindingCount);
        writer.WriteNumber("securitySensitiveFindingCount", evidence.SecuritySensitiveFindingCount);
        writer.WriteNumber("unsupportedFindingCount", evidence.UnsupportedFindingCount);

        // Lifecycle
        writer.WriteString("maxLifecycleDecision", evidence.MaxLifecycleDecision.ToString());
        writer.WriteBoolean("requiresReview", evidence.RequiresReview);
        writer.WriteBoolean("isBlocked", evidence.IsBlocked);
        writer.WriteNumber("packageFindingCount", evidence.PackageFindingCount);

        // Normalized findings
        writer.WritePropertyName("normalizedFindings");
        writer.WriteStartArray();
        foreach (var f in evidence.NormalizedFindings
            .OrderBy(f => f.Severity, StringComparer.Ordinal)
            .ThenBy(f => f.Code, StringComparer.Ordinal)
            .ThenBy(f => f.Source, StringComparer.Ordinal)
            .ThenBy(f => f.Message, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("severity", f.Severity);
            writer.WriteString("code", f.Code);
            writer.WriteString("source", f.Source);
            writer.WriteString("message", f.Message);

            // Subject
            if (f.Subject is null)
            {
                writer.WriteNull("subject");
            }
            else
            {
                writer.WritePropertyName("subject");
                WriteDescriptorRef(writer, f.Subject.Value);
            }

            // RelatedRefs
            writer.WritePropertyName("relatedRefs");
            writer.WriteStartArray();
            foreach (var r in f.RelatedRefs
                .OrderBy(r => r.Namespace, StringComparer.Ordinal)
                .ThenBy(r => r.Id, StringComparer.Ordinal)
                .ThenBy(r => r.Version))
            {
                writer.WriteStartObject();
                writer.WriteString("namespace", r.Namespace);
                writer.WriteString("id", r.Id);
                if (r.Version is null)
                    writer.WriteNull("version");
                else
                    writer.WriteNumber("version", r.Version.Value);
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
            writer.WriteString("severity", c.Severity);
            writer.WriteString("code", c.Code);
            writer.WriteNumber("count", c.Count);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteDescriptorRef(Utf8JsonWriter writer, DescriptorRef r)
    {
        writer.WriteStartObject();
        writer.WriteString("namespace", r.Namespace);
        writer.WriteString("id", r.Id);
        if (r.Version is null)
            writer.WriteNull("version");
        else
            writer.WriteNumber("version", r.Version.Value);
        writer.WriteEndObject();
    }
}
