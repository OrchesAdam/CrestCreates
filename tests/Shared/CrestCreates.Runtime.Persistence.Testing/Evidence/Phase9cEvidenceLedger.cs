namespace CrestCreates.Runtime.Persistence.Testing.Evidence;

using CrestCreates.Runtime.Persistence.Testing.Manifest;

public sealed record Phase9cEvidenceEntry(
    string AcceptanceName,
    string Runner,
    string EvidenceVector,
    bool Passed,
    string Source);

public sealed class Phase9cEvidenceLedger
{
    private readonly List<Phase9cEvidenceEntry> _entries = [];
    public IReadOnlyList<Phase9cEvidenceEntry> Entries => _entries;

    public void Record(Phase9cEvidenceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!Phase9cAcceptanceManifest.NormativeNames.Contains(entry.AcceptanceName, StringComparer.Ordinal)
            && !Phase9cSupplementalAcceptanceManifest.Names.Contains(entry.AcceptanceName, StringComparer.Ordinal))
            throw new ArgumentException($"Unknown Phase 9c acceptance '{entry.AcceptanceName}'.", nameof(entry));
        if (_entries.Any(existing => string.Equals(existing.AcceptanceName, entry.AcceptanceName, StringComparison.Ordinal)
            && string.Equals(existing.Runner, entry.Runner, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Duplicate Phase 9c evidence ownership for '{entry.AcceptanceName}' and runner '{entry.Runner}'.");
        _entries.Add(entry);
    }

    /// <summary>
    /// Records evidence only after the named runner has executed its assertion.
    /// This keeps the ledger from being used as a list of manually asserted
    /// <c>Passed: true</c> claims.
    /// </summary>
    public void RecordExecutable(
        string acceptanceName,
        string runner,
        string evidenceVector,
        string source,
        Func<bool> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var passed = run();
        Record(new Phase9cEvidenceEntry(acceptanceName, runner, evidenceVector, passed, source));
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

    public void ValidateNormativeCompleteness()
    {
        var missing = Phase9cAcceptanceManifest.NormativeNames
            .Where(name => !_entries.Any(entry => string.Equals(entry.AcceptanceName, name, StringComparison.Ordinal)))
            .ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException($"Phase 9c evidence is incomplete; missing {missing.Length} normative acceptance entries.");
    }

    /// <summary>
    /// Validates the executable evidence subset used by CI.  A ledger entry is
    /// not documentation unless it points at a real test/fixture source and is
    /// marked passed by that runner.
    /// </summary>
    public void ValidateExecutableEvidence(IEnumerable<string> requiredAcceptanceNames)
    {
        ArgumentNullException.ThrowIfNull(requiredAcceptanceNames);
        var missing = requiredAcceptanceNames
            .Distinct(StringComparer.Ordinal)
            .Where(name => !_entries.Any(entry =>
                string.Equals(entry.AcceptanceName, name, StringComparison.Ordinal)
                && entry.Passed
                && !string.IsNullOrWhiteSpace(entry.Runner)
                && !string.IsNullOrWhiteSpace(entry.Source)
                && File.Exists(entry.Source)))
            .ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException(
                $"Phase 9c executable evidence is incomplete; missing {missing.Length} bound acceptance entries: {string.Join(", ", missing)}.");
    }

    public void ValidateFrozenClosure()
    {
        ValidateFrozenManifest();
        var expected = Phase9cAcceptanceManifest.NormativeNames
            .Concat(Phase9cSupplementalAcceptanceManifest.Names)
            .ToHashSet(StringComparer.Ordinal);
        var actual = _entries.Select(entry => entry.AcceptanceName).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected) || _entries.Count != expected.Count)
            throw new InvalidOperationException($"Phase 9c evidence closure is incomplete; expected exactly {expected.Count} normative/supplemental tuples but recorded {_entries.Count}.");
        ValidateExecutableEvidence(expected);
    }
}
