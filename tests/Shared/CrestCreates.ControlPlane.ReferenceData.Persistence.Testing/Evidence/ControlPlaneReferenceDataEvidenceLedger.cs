using System.Collections.Concurrent;

namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

/// <summary>
/// Executed-tuple accounting ledger for the Phase 9b+ Control Plane and
/// Reference Data acceptance evidence.
///
/// The invariant this ledger enforces is: one real test invocation generates
/// one executed tuple. Test methods call <see cref="Record"/> with the exact
/// CaseId / Surface / Variant / EvidenceVectorKey / Runner they actually
/// exercised, and the recorded tuples are written through to a per-assembly
/// evidence file under tests/artifacts/control-plane-evidence/. The Boundary
/// gate then asserts Required(manifest) == Executed(files) without any source
/// scanning: a test method merely existing can no longer sign for tuples its
/// theory rows never ran.
/// </summary>
public static class ControlPlaneReferenceDataEvidenceLedger
{
    private const string ArtifactsDirectory = "control-plane-evidence";
    private static readonly object Sync = new();
    private static readonly HashSet<string> Recorded = new(StringComparer.Ordinal);

    /// <summary>
    /// Records one executed evidence tuple. Safe to call concurrently from
    /// parallel xUnit classes within one test host; write-through is append-only.
    /// </summary>
    public static void Record(
        string caseId,
        string surface,
        string variant,
        EvidenceVectorKey key,
        RequiredRunner runner)
    {
        var line = EvidenceTupleKey(caseId, surface, variant, key, runner);
        lock (Sync)
        {
            if (!Recorded.Add(line))
                return;

            var path = EvidenceFileForEntryAssembly();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(line);
        }
    }

    public static void Record(EvidenceTuple tuple)
        => Record(tuple.CaseId, tuple.Surface, tuple.Variant, tuple.Key, tuple.Runner);

    /// <summary>
    /// The set of executed tuples currently recorded in this test-host process.
    /// </summary>
    public static IReadOnlySet<string> InProcessExecutedKeys
    {
        get
        {
            lock (Sync)
                return new HashSet<string>(Recorded, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Reads the union of every executed tuple across all evidence files written
    /// by every test project that ran in this environment.
    /// </summary>
    public static IReadOnlySet<string> ReadExecutedTupleKeys()
    {
        var directory = EvidenceDirectory();
        if (!Directory.Exists(directory))
            return new HashSet<string>(StringComparer.Ordinal);

        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "executed-*.jsonl"))
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                    union.Add(trimmed);
            }
        }

        return union;
    }

    /// <summary>
    /// Deletes every evidence file. Run at the start of a full suite so stale
    /// files from a previous run can never satisfy the gate.
    /// </summary>
    public static void Reset()
    {
        lock (Sync)
            Recorded.Clear();

        var directory = EvidenceDirectory();
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(directory, "executed-*.jsonl"))
            File.Delete(file);
    }

    public static string EvidenceTupleKey(
        string caseId,
        string surface,
        string variant,
        EvidenceVectorKey key,
        RequiredRunner runner)
        => string.Join('|', caseId, surface, variant, key.ToString(), runner.ToString());

    public static string EvidenceTupleKey(EvidenceTuple tuple)
        => EvidenceTupleKey(tuple.CaseId, tuple.Surface, tuple.Variant, tuple.Key, tuple.Runner);

    /// <summary>
    /// The directory all evidence projects write to. Discovered from the
    /// repository root so every test host (regardless of bin path) agrees.
    /// </summary>
    public static string EvidenceDirectory()
        => Path.Combine(FindRepositoryRoot(), "tests", "artifacts", ArtifactsDirectory);

    private static string EvidenceFileForEntryAssembly()
    {
        var entry = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";
        return Path.Combine(EvidenceDirectory(), $"executed-{entry}.jsonl");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
            current = current.Parent;

        return current?.FullName
            ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }
}