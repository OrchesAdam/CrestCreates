using System.Text.RegularExpressions;
using CrestCreates.Agent.Memory.Persistence.Testing.Manifest;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Proves the frozen evidence ledger exactly matches the approved Design's
/// §17 acceptance tables: 59 Case IDs (H01-H09, B01-B18, F01-F16, C01-C16),
/// 98 RequiredEvidence tuples, and the frozen per-kind counts. The parser
/// stops at §18 so later numbers cannot satisfy the ledger accidentally.
/// </summary>
public class DurableAgentMemoryEvidenceManifestTests
{
    private static readonly string SpecPath = Path.Combine(
        DependencyBoundaryTestsHelpers.FindRepoRoot().FullName,
        "docs/superpowers/specs/2026-08-13-phase-9bplus-durable-agent-memory-store-provider-design.md");

    [Fact]
    public void ManifestCaseIds_Should_ExactlyMatchSpec17Tables()
    {
        var specIds = ParseCaseIdsFromSpec();
        var manifestIds = DurableAgentMemoryCaseManifest.Cases
            .Select(caseItem => caseItem.CaseId)
            .ToArray();

        Assert.True(
            specIds.Length == DurableAgentMemoryCaseManifest.CaseCount,
            $"Spec §17 must define exactly {DurableAgentMemoryCaseManifest.CaseCount} cases, got {specIds.Length}.");
        Assert.True(
            specIds.SequenceEqual(manifestIds, StringComparer.Ordinal),
            $"Spec §17 Case IDs and the manifest must be identical.{Environment.NewLine}"
            + $"Spec: {string.Join(", ", specIds)}{Environment.NewLine}"
            + $"Manifest: {string.Join(", ", manifestIds)}");
        Assert.True(
            specIds.Distinct(StringComparer.Ordinal).Count() == specIds.Length,
            "Spec §17 Case IDs must not contain duplicates.");
    }

    [Fact]
    public void ManifestEvidence_Should_HaveExactly98UniqueTuples()
    {
        var tuples = DurableAgentMemoryCaseManifest.EvidenceTuples;
        Assert.True(
            tuples.Count == DurableAgentMemoryCaseManifest.EvidenceTupleCount,
            $"Manifest must contain exactly {DurableAgentMemoryCaseManifest.EvidenceTupleCount} evidence tuples, got {tuples.Count}.");

        var duplicates = tuples
            .GroupBy(tuple => (tuple.CaseId, tuple.Kind, tuple.ExactFullyQualifiedTestName))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.True(
            duplicates.Length == 0,
            $"Evidence tuples must be unique by (CaseId, EvidenceKind, FQN): {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void ManifestEvidenceCounts_Should_MatchFrozenPerKindCounts()
    {
        var counts = DurableAgentMemoryCaseManifest.EvidenceCountByKind;
        Assert.Equal(28, counts[DurableAgentMemoryEvidenceKind.InMemorySemantic]);
        Assert.Equal(24, counts[DurableAgentMemoryEvidenceKind.PostgreSqlSemantic]);
        Assert.Equal(5, counts[DurableAgentMemoryEvidenceKind.PostgreSqlConcurrency]);
        Assert.Equal(8, counts[DurableAgentMemoryEvidenceKind.PostgreSqlRestart]);
        Assert.Equal(10, counts[DurableAgentMemoryEvidenceKind.PostgreSqlFailureInjection]);
        Assert.Equal(2, counts[DurableAgentMemoryEvidenceKind.CrashWorker]);
        Assert.Equal(5, counts[DurableAgentMemoryEvidenceKind.PostgreSqlComposition]);
        Assert.Equal(3, counts[DurableAgentMemoryEvidenceKind.AccountabilityComposition]);
        Assert.Equal(5, counts[DurableAgentMemoryEvidenceKind.RecallExpansionParity]);
        Assert.Equal(2, counts[DurableAgentMemoryEvidenceKind.Migration]);
        Assert.Equal(1, counts[DurableAgentMemoryEvidenceKind.JsonArchitecture]);
        Assert.Equal(3, counts[DurableAgentMemoryEvidenceKind.Boundary]);
        Assert.Equal(1, counts[DurableAgentMemoryEvidenceKind.NativeAot]);
        Assert.Equal(1, counts[DurableAgentMemoryEvidenceKind.CanonicalBuild]);
        var total = counts.Values.Sum();
        Assert.True(
            total == 98,
            $"Per-kind evidence counts must sum to the frozen total of 98, got {total}.");
    }

    [Fact]
    public void EveryCase_Should_HaveAtLeastOneEvidenceTuple()
    {
        var empty = DurableAgentMemoryCaseManifest.Cases
            .Where(caseItem => caseItem.Evidence.Count == 0)
            .Select(caseItem => caseItem.CaseId)
            .ToArray();
        Assert.True(empty.Length == 0, $"Every Case must have at least one requirement: {string.Join(", ", empty)}");
    }

    [Fact]
    public void EvidenceOwningSlices_Should_BeWithin2To11()
    {
        var invalid = DurableAgentMemoryCaseManifest.EvidenceTuples
            .Where(tuple => tuple.OwningSlice is < 2 or > 11)
            .Select(tuple => $"{tuple.CaseId}/{tuple.Kind}@{tuple.OwningSlice}")
            .ToArray();
        Assert.True(invalid.Length == 0, $"Evidence owning Slices must be within 2-11: {string.Join(", ", invalid)}");
    }

    [Fact]
    public void EvidenceTuples_Should_CarryExactFullyQualifiedTestNames()
    {
        var malformed = DurableAgentMemoryCaseManifest.EvidenceTuples
            .Where(tuple => tuple.ExactFullyQualifiedTestName.Count(character => character == '.') < 2
                || string.IsNullOrWhiteSpace(tuple.ExactFullyQualifiedTestName))
            .Select(tuple => $"{tuple.CaseId}/{tuple.Kind}")
            .ToArray();
        Assert.True(malformed.Length == 0, $"Evidence tuples must carry a namespace-qualified FQN: {string.Join(", ", malformed)}");
    }

    private static string[] ParseCaseIdsFromSpec()
    {
        Assert.True(File.Exists(SpecPath), $"Spec not found: {SpecPath}");
        var lines = File.ReadAllLines(SpecPath);
        var start = Array.FindIndex(lines, line => line.StartsWith("### 17.1", StringComparison.Ordinal));
        Assert.True(start >= 0, "Spec §17.1 heading not found.");
        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("## 18.", StringComparison.Ordinal));
        if (end < 0) end = lines.Length;

        var row = new Regex(@"^\|\s*(H\d\d|B\d\d|F\d\d|C\d\d)\s*\|");
        return lines
            .Skip(start)
            .Take(end - start)
            .Select(line => row.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
