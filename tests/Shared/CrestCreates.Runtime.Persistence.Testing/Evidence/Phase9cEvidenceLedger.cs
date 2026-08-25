using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.Runtime.Persistence.Testing.Manifest;

namespace CrestCreates.Runtime.Persistence.Testing.Evidence;

public sealed record Phase9cEvidenceEntry(
    string CaseId,
    string AcceptanceName,
    string Runner,
    string EvidenceVector,
    bool Passed,
    string Source);

/// <summary>
/// Records evidence tuples produced by the assertion that executed a case.
/// There is deliberately no runner-batch API: project completion is not case
/// completion.  CI may merge the JSONL artifacts emitted by each test process
/// and compare the exact tuple set with <see cref="RequiredTuples"/>.
/// </summary>
public sealed class Phase9cEvidenceLedger
{
    private readonly List<Phase9cEvidenceEntry> _entries = [];
    public IReadOnlyList<Phase9cEvidenceEntry> Entries => _entries;

    public void Record(Phase9cEvidenceTuple tuple, bool passed, string source)
    {
        ArgumentNullException.ThrowIfNull(tuple);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!Phase9cEvidenceRunnerCatalog.RequiredTuples.Contains(tuple))
            throw new ArgumentException($"Unknown Phase 9c evidence tuple '{tuple.CaseId}/{tuple.AcceptanceName}/{tuple.Runner}'.", nameof(tuple));
        if (_entries.Any(existing => SameTuple(existing, tuple)))
            throw new InvalidOperationException($"Duplicate Phase 9c evidence tuple '{tuple.CaseId}/{tuple.AcceptanceName}/{tuple.Runner}'.");
        _entries.Add(new Phase9cEvidenceEntry(tuple.CaseId, tuple.AcceptanceName, tuple.Runner, tuple.EvidenceVector, passed, source));
    }

    public void RecordExecutable(Phase9cEvidenceTuple tuple, string source, Func<bool> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        Record(tuple, run(), source);
    }

    public void WriteJsonLines(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.AppendAllLines(path, _entries.Select(entry => JsonSerializer.Serialize(entry, Phase9cEvidenceJsonContext.Default.Phase9cEvidenceEntry)));
    }

    public static Phase9cEvidenceLedger ReadJsonLines(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var ledger = new Phase9cEvidenceLedger();
        foreach (var path in paths.Where(File.Exists))
            foreach (var line in File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                var entry = JsonSerializer.Deserialize(line, Phase9cEvidenceJsonContext.Default.Phase9cEvidenceEntry)
                    ?? throw new InvalidOperationException($"Invalid Phase 9c evidence record in '{path}'.");
                ledger.Record(new Phase9cEvidenceTuple(entry.CaseId, entry.AcceptanceName, entry.Runner, entry.EvidenceVector), entry.Passed, entry.Source);
            }
        return ledger;
    }

    public static void ValidateFrozenManifest()
    {
        var normative = Phase9cAcceptanceManifest.NormativeNames;
        var supplemental = Phase9cSupplementalAcceptanceManifest.Names;
        if (normative.Count != 145 || supplemental.Count != 25)
            throw new InvalidOperationException($"Phase 9c frozen manifest cardinality drifted (normative={normative.Count}, supplemental={supplemental.Count}).");
        if (normative.Count != normative.Distinct(StringComparer.Ordinal).Count()
            || supplemental.Count != supplemental.Distinct(StringComparer.Ordinal).Count()
            || normative.Intersect(supplemental, StringComparer.Ordinal).Any())
            throw new InvalidOperationException("Phase 9c frozen manifest contains duplicate or overlapping acceptance names.");
    }

    public void ValidateFrozenClosure()
    {
        ValidateFrozenManifest();
        var expected = Phase9cEvidenceRunnerCatalog.RequiredTuples.ToHashSet();
        var actual = _entries.Select(entry => new Phase9cEvidenceTuple(entry.CaseId, entry.AcceptanceName, entry.Runner, entry.EvidenceVector)).ToHashSet();
        if (!actual.SetEquals(expected) || _entries.Count != expected.Count)
        {
            var missing = expected.Except(actual).Select(Format).Take(12);
            var unexpected = actual.Except(expected).Select(Format).Take(12);
            throw new InvalidOperationException($"Phase 9c evidence closure is incomplete; expected {expected.Count} exact tuples, recorded {_entries.Count}. Missing: {string.Join(", ", missing)}. Unexpected: {string.Join(", ", unexpected)}.");
        }
        var failed = _entries.Where(entry => !entry.Passed).Select(entry => Format(new Phase9cEvidenceTuple(entry.CaseId, entry.AcceptanceName, entry.Runner, entry.EvidenceVector))).ToArray();
        if (failed.Length != 0) throw new InvalidOperationException($"Phase 9c evidence contains failed tuples: {string.Join(", ", failed)}.");
    }

    private static bool SameTuple(Phase9cEvidenceEntry entry, Phase9cEvidenceTuple tuple)
        => string.Equals(entry.CaseId, tuple.CaseId, StringComparison.Ordinal)
            && string.Equals(entry.AcceptanceName, tuple.AcceptanceName, StringComparison.Ordinal)
            && string.Equals(entry.Runner, tuple.Runner, StringComparison.Ordinal)
            && string.Equals(entry.EvidenceVector, tuple.EvidenceVector, StringComparison.Ordinal);

    private static string Format(Phase9cEvidenceTuple tuple) => $"{tuple.CaseId}/{tuple.AcceptanceName}/{tuple.Runner}/{tuple.EvidenceVector}";
}

[JsonSerializable(typeof(Phase9cEvidenceEntry))]
internal partial class Phase9cEvidenceJsonContext : JsonSerializerContext;
