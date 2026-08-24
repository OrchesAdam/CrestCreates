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

    public void ValidateNormativeCompleteness()
    {
        var missing = Phase9cAcceptanceManifest.NormativeNames
            .Where(name => !_entries.Any(entry => string.Equals(entry.AcceptanceName, name, StringComparison.Ordinal)))
            .ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException($"Phase 9c evidence is incomplete; missing {missing.Length} normative acceptance entries.");
    }
}
