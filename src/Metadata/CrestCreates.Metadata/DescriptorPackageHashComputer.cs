using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

/// <summary>
/// AoT-safe deterministic hash computer for descriptor packages.
/// All hashes use explicit string concatenation with delimiter escaping,
/// null sentinels, ordinal ordering, and invariant formatting — no runtime JSON.
///
/// <b>Note</b>: This uses the legacy pipe-delimited hash format. Descriptor hashes
/// now use the canonical JSON hash runtime (<see cref="ICanonicalHashComputer"/>).
/// Package hash migration to canonical JSON is planned for v2.
/// </summary>
[Obsolete("Pipe-delimited hash format — migrate to ICanonicalHashComputer in v2.")]
public static class DescriptorPackageHashComputer
{
    private const string NullSentinel = "\\0";

    private static string Esc(string value) =>
        value.Replace("\\", "\\\\").Replace("|", "\\|");

    private static void AppendField(StringBuilder sb, string? value)
    {
        sb.Append(value is null ? NullSentinel : Esc(value));
        sb.Append('|');
    }

    private static void AppendField(StringBuilder sb, int value)
    {
        sb.Append(value.ToString(CultureInfo.InvariantCulture));
        sb.Append('|');
    }

    private static void AppendField(StringBuilder sb, int? value)
    {
        sb.Append(value?.ToString(CultureInfo.InvariantCulture) ?? NullSentinel);
        sb.Append('|');
    }

    private static void AppendField(StringBuilder sb, bool value)
    {
        sb.Append(value ? '1' : '0');
        sb.Append('|');
    }

    // ── Content Hash ───────────────────────────────────────────

    public static string ComputeContentHash(
        string formatVersion,
        IReadOnlyList<DescriptorManifestEntry> entries,
        IReadOnlyList<DescriptorPackageRelationshipEntry> relationships)
    {
        var sb = new StringBuilder();
        AppendField(sb, formatVersion);

        var sortedEntries = entries
            .OrderBy(e => e.Ref.Namespace, StringComparer.Ordinal)
            .ThenBy(e => e.Ref.Id, StringComparer.Ordinal)
            .ThenBy(e => e.Ref.Version ?? 0);

        foreach (var e in sortedEntries)
        {
            AppendField(sb, e.Ref.Namespace);
            AppendField(sb, e.Ref.Id);
            AppendField(sb, e.Ref.Version);
            AppendField(sb, (int)e.Kind);
            AppendField(sb, (int)e.State);
        }

        var sortedRels = relationships
            .OrderBy(r => r.From.Namespace, StringComparer.Ordinal)
            .ThenBy(r => r.From.Id, StringComparer.Ordinal)
            .ThenBy(r => r.From.Version ?? 0)
            .ThenBy(r => r.To.Namespace, StringComparer.Ordinal)
            .ThenBy(r => r.To.Id, StringComparer.Ordinal)
            .ThenBy(r => r.To.Version ?? 0)
            .ThenBy(r => r.Kind)
            .ThenBy(r => r.Strength)
            .ThenBy(r => r.Role ?? "", StringComparer.Ordinal)
            .ThenBy(r => r.SourcePath ?? "", StringComparer.Ordinal)
            .ThenBy(r => r.IsRuntimeBinding);

        foreach (var r in sortedRels)
        {
            AppendField(sb, r.From.Namespace);
            AppendField(sb, r.From.Id);
            AppendField(sb, r.From.Version);
            AppendField(sb, r.To.Namespace);
            AppendField(sb, r.To.Id);
            AppendField(sb, r.To.Version);
            AppendField(sb, (int)r.Kind);
            AppendField(sb, (int)r.Strength);
            AppendField(sb, r.Role);
            AppendField(sb, r.SourcePath);
            AppendField(sb, r.IsRuntimeBinding);
        }

        return ComputeSha256(sb.ToString());
    }

    // ── Evidence Hash ──────────────────────────────────────────

    public static string ComputeEvidenceHash(DescriptorPackageEvidence evidence)
    {
        var sb = new StringBuilder();

        AppendField(sb, evidence.TopologyNodeCount);
        AppendField(sb, evidence.TopologyEdgeCount);
        AppendField(sb, evidence.HasTopologyErrors);

        foreach (var dc in evidence.TopologyDiagnosticCounts
                     .OrderBy(d => d.Severity, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal))
        {
            AppendField(sb, dc.Severity);
            AppendField(sb, dc.Code);
            AppendField(sb, dc.Count);
        }

        AppendField(sb, (int)evidence.MaxImpactSeverity);
        AppendField(sb, evidence.AffectedDescriptorCount);
        AppendField(sb, evidence.ImpactPathCount);

        foreach (var dc in evidence.ImpactDiagnosticCounts
                     .OrderBy(d => d.Severity, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal))
        {
            AppendField(sb, dc.Severity);
            AppendField(sb, dc.Code);
            AppendField(sb, dc.Count);
        }

        AppendField(sb, (int)evidence.MaxCompatibilityLevel);
        AppendField(sb, evidence.BreakingFindingCount);
        AppendField(sb, evidence.SecuritySensitiveFindingCount);
        AppendField(sb, evidence.UnsupportedFindingCount);
        AppendField(sb, (int)evidence.MaxLifecycleDecision);
        AppendField(sb, evidence.RequiresReview);
        AppendField(sb, evidence.IsBlocked);
        AppendField(sb, evidence.PackageFindingCount);

        var sortedFindings = evidence.NormalizedFindings
            .OrderBy(f => f.Source, StringComparer.Ordinal)
            .ThenBy(f => f.Code, StringComparer.Ordinal)
            .ThenBy(f => f.Severity, StringComparer.Ordinal)
            .ThenBy(f => f.Subject?.FullId ?? NullSentinel, StringComparer.Ordinal)
            .ThenBy(f => f.Message, StringComparer.Ordinal)
            .ThenBy(f => RelatedRefsCanonicalKey(f), StringComparer.Ordinal);

        foreach (var f in sortedFindings)
        {
            AppendField(sb, f.Source);
            AppendField(sb, f.Code);
            AppendField(sb, f.Severity);
            AppendField(sb, f.Subject?.FullId);
            AppendField(sb, f.Message);

            // Related refs in canonical order — each ref as individual fields
            var sortedRelated = f.RelatedRefs
                .OrderBy(r => r.Namespace, StringComparer.Ordinal)
                .ThenBy(r => r.Id, StringComparer.Ordinal)
                .ThenBy(r => r.Version ?? 0);
            foreach (var rr in sortedRelated)
            {
                AppendField(sb, rr.Namespace);
                AppendField(sb, rr.Id);
                AppendField(sb, rr.Version);
            }
        }

        return ComputeSha256(sb.ToString());
    }

    // ── Envelope Hash ──────────────────────────────────────────

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
        AppendField(sb, contentHash);
        AppendField(sb, evidenceHash);
        AppendField(sb, packageId);
        AppendField(sb, packageVersion);
        AppendField(sb, createdAt.ToString("O", CultureInfo.InvariantCulture));
        AppendField(sb, createdBy);
        AppendField(sb, source);

        return ComputeSha256(sb.ToString());
    }

    // ── Hashing ────────────────────────────────────────────────

    private static string RelatedRefsCanonicalKey(EvidenceFinding f)
    {
        if (f.RelatedRefs.Count == 0)
            return NullSentinel;

        var sb = new StringBuilder();
        foreach (var rr in f.RelatedRefs
                     .OrderBy(r => r.Namespace, StringComparer.Ordinal)
                     .ThenBy(r => r.Id, StringComparer.Ordinal)
                     .ThenBy(r => r.Version ?? 0))
        {
            AppendField(sb, rr.Namespace);
            AppendField(sb, rr.Id);
            AppendField(sb, rr.Version);
        }
        return sb.ToString();
    }

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
