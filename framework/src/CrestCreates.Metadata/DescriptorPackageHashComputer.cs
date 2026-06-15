using System.Security.Cryptography;
using System.Text;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public static class DescriptorPackageHashComputer
{
    public static string ComputeContentHash(
        string formatVersion,
        IReadOnlyList<DescriptorManifestEntry> entries,
        IReadOnlyList<DescriptorPackageRelationshipEntry> relationships)
    {
        var sb = new StringBuilder();
        sb.Append(formatVersion);
        sb.Append('|');

        var sortedRefs = entries
            .OrderBy(e => e.Ref.Namespace)
            .ThenBy(e => e.Ref.Id)
            .ThenBy(e => e.Ref.Version ?? 0)
            .Select(e => $"{e.Ref.Namespace}:{e.Ref.Id}:{e.Ref.Version}:{e.Kind}:{e.State}")
            .ToList();

        sb.AppendJoin("||", sortedRefs);
        sb.Append('|');

        var sortedRels = relationships
            .OrderBy(r => r.From.Namespace)
            .ThenBy(r => r.From.Id)
            .ThenBy(r => r.From.Version ?? 0)
            .ThenBy(r => r.To.Namespace)
            .ThenBy(r => r.To.Id)
            .ThenBy(r => r.To.Version ?? 0)
            .ThenBy(r => r.Kind)
            .ThenBy(r => r.Strength)
            .ThenBy(r => r.Role ?? "")
            .ThenBy(r => r.SourcePath ?? "")
            .ThenBy(r => r.IsRuntimeBinding)
            .Select(r =>
                $"{r.From.Namespace}:{r.From.Id}:{r.From.Version}→" +
                $"{r.To.Namespace}:{r.To.Id}:{r.To.Version}:{r.Kind}:{r.Strength}" +
                $":{r.Role}:{r.SourcePath}:{r.IsRuntimeBinding}")
            .ToList();

        sb.AppendJoin("||", sortedRels);

        return ComputeSha256(sb.ToString());
    }

    public static string ComputeEvidenceHash(DescriptorPackageEvidence evidence)
    {
        var sb = new StringBuilder();

        sb.Append(evidence.TopologyNodeCount);
        sb.Append('|');
        sb.Append(evidence.TopologyEdgeCount);
        sb.Append('|');
        sb.Append(evidence.HasTopologyErrors);
        sb.Append('|');

        var sortedTopologyDiagnosticCounts = evidence.TopologyDiagnosticCounts
            .OrderBy(d => d.Severity).ThenBy(d => d.Code);
        foreach (var dc in sortedTopologyDiagnosticCounts)
        {
            sb.Append(dc.Severity); sb.Append(':');
            sb.Append(dc.Code); sb.Append(':');
            sb.Append(dc.Count);
            sb.Append('|');
        }

        sb.Append(evidence.MaxImpactSeverity);
        sb.Append('|');
        sb.Append(evidence.AffectedDescriptorCount);
        sb.Append('|');
        sb.Append(evidence.ImpactPathCount);
        sb.Append('|');

        var sortedImpactDiagnosticCounts = evidence.ImpactDiagnosticCounts
            .OrderBy(d => d.Severity).ThenBy(d => d.Code);
        foreach (var dc in sortedImpactDiagnosticCounts)
        {
            sb.Append(dc.Severity); sb.Append(':');
            sb.Append(dc.Code); sb.Append(':');
            sb.Append(dc.Count);
            sb.Append('|');
        }

        sb.Append(evidence.MaxCompatibilityLevel);
        sb.Append('|');
        sb.Append(evidence.BreakingFindingCount);
        sb.Append('|');
        sb.Append(evidence.SecuritySensitiveFindingCount);
        sb.Append('|');
        sb.Append(evidence.UnsupportedFindingCount);
        sb.Append('|');
        sb.Append(evidence.MaxLifecycleDecision);
        sb.Append('|');
        sb.Append(evidence.RequiresReview);
        sb.Append('|');
        sb.Append(evidence.IsBlocked);
        sb.Append('|');
        sb.Append(evidence.PackageFindingCount);
        sb.Append('|');

        var sortedNormalizedFindings = evidence.NormalizedFindings
            .OrderBy(f => f.Source).ThenBy(f => f.Code).ThenBy(f => f.Severity)
            .ThenBy(f => f.Subject?.FullId ?? "").ThenBy(f => f.Message)
            .ThenBy(f => string.Join(',',
                f.RelatedRefs
                    .OrderBy(r => r.Namespace).ThenBy(r => r.Id).ThenBy(r => r.Version ?? 0)
                    .Select(r => r.FullId)));
        foreach (var f in sortedNormalizedFindings)
        {
            sb.Append(f.Source); sb.Append(':');
            sb.Append(f.Code); sb.Append(':');
            sb.Append(f.Severity); sb.Append(':');
            sb.Append(f.Subject?.FullId ?? ""); sb.Append(':');
            sb.Append(f.Message); sb.Append(':');
            var sortedRelated = f.RelatedRefs
                .OrderBy(r => r.Namespace).ThenBy(r => r.Id).ThenBy(r => r.Version ?? 0);
            sb.AppendJoin(',', sortedRelated.Select(r => r.FullId));
            sb.Append('|');
        }

        return ComputeSha256(sb.ToString());
    }

    public static string ComputeEnvelopeHash(
        string contentHash,
        string evidenceHash,
        string packageId,
        string packageVersion,
        DateTimeOffset createdAt,
        string? createdBy,
        string? source)
    {
        var sb = new StringBuilder();
        sb.Append(contentHash);
        sb.Append('|');
        sb.Append(evidenceHash);
        sb.Append('|');
        sb.Append(packageId);
        sb.Append('|');
        sb.Append(packageVersion);
        sb.Append('|');
        sb.Append(createdAt.ToString("O"));
        sb.Append('|');
        sb.Append(createdBy ?? "");
        sb.Append('|');
        sb.Append(source ?? "");

        return ComputeSha256(sb.ToString());
    }

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
