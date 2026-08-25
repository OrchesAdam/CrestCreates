namespace CrestCreates.Runtime.Persistence.Testing.Manifest;

/// <summary>
/// Frozen evidence is a set of case/surface tuples. A green project is only
/// one part of a tuple and cannot manufacture the other parts.
/// </summary>
public sealed record Phase9cEvidenceTuple(string CaseId, string AcceptanceName, string Runner, string EvidenceVector);

public static class Phase9cEvidenceRunnerCatalog
{
    public static IReadOnlySet<string> RunnerNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    { "SH", "IM", "PG", "WF", "HT", "DEL", "ACCT", "PROC", "ACT", "BND", "CW", "AOT" };

    public static IReadOnlyList<Phase9cEvidenceTuple> RequiredTuples { get; } = BuildRequiredTuples();

    public static IReadOnlyList<Phase9cEvidenceTuple> ForAcceptance(string acceptanceName)
        => RequiredTuples.Where(tuple => string.Equals(tuple.AcceptanceName, acceptanceName, StringComparison.Ordinal)).ToArray();

    private static IReadOnlyList<Phase9cEvidenceTuple> BuildRequiredTuples()
    {
        var tuples = new List<Phase9cEvidenceTuple>();
        foreach (var name in Phase9cAcceptanceManifest.NormativeNames.Concat(Phase9cSupplementalAcceptanceManifest.Names))
        {
            if (name == "WorkflowContinuationAcceptance_Integrity_Should_Use_FrozenV1Projection")
            {
                Add(tuples, "RCA01", name, "WF", "semantic"); Add(tuples, "RCA01", name, "SH", "semantic");
                Add(tuples, "RCA01", name, "IM", "semantic"); Add(tuples, "RCA01", name, "PG", "semantic");
                Add(tuples, "RCA01", name, "BND", "composition");
                continue;
            }
            if (name == "Same_CompletionEventId_WithChangedOutcomeOrResult_Should_Conflict")
            {
                Add(tuples, "RCA02", name, "WF", "semantic"); Add(tuples, "RCA02", name, "SH", "semantic");
                Add(tuples, "RCA02", name, "IM", "semantic"); Add(tuples, "RCA02", name, "PG", "semantic");
                continue;
            }
            var (caseId, runners, vector) = Classify(name);
            foreach (var runner in runners) Add(tuples, caseId, name, runner, vector);
        }
        return tuples;
    }

    private static void Add(List<Phase9cEvidenceTuple> tuples, string caseId, string name, string runner, string vector)
    {
        if (!RunnerNames.Contains(runner)) throw new InvalidOperationException($"Unknown Phase 9c evidence runner '{runner}'.");
        tuples.Add(new Phase9cEvidenceTuple(caseId, name, runner, vector));
    }

    private static (string CaseId, IReadOnlyList<string> Runners, string Vector) Classify(string name)
    {
        if (name.StartsWith("ARCH", StringComparison.Ordinal)) return (CaseId(name, "ARCH", 16), ["DEL", "BND"], "composition");
        if (name.StartsWith("V012", StringComparison.Ordinal) || name.StartsWith("Persisted_", StringComparison.Ordinal) || name.StartsWith("Required_Workflow", StringComparison.Ordinal) || name.StartsWith("WorkflowContinuationAcceptance_Should", StringComparison.Ordinal) || name.StartsWith("Optional_LocalEventFailure", StringComparison.Ordinal) || name.StartsWith("ActiveCompositionProbe", StringComparison.Ordinal) || name.StartsWith("PostgreSqlOutboxFixture", StringComparison.Ordinal) || name.StartsWith("NativeBinary", StringComparison.Ordinal)) return ("N01", ["PG", "AOT"], "native");
        if (name.StartsWith("PROC", StringComparison.Ordinal) || name.Contains("Procurement", StringComparison.Ordinal)) return ("PROC01", ["PROC"], "semantic");
        if (name.StartsWith("ACT", StringComparison.Ordinal) || name.StartsWith("Activation", StringComparison.Ordinal)) return ("ACT01", ["ACT"], "semantic");
        if (name.StartsWith("BOOT", StringComparison.Ordinal) || name.StartsWith("DB_Composition", StringComparison.Ordinal) || name.StartsWith("HumanTaskObligation", StringComparison.Ordinal)) return ("BOOT01", ["DEL", "PG", "BND"], "composition");
        if (name.StartsWith("SCHEMA", StringComparison.Ordinal) || name.Contains("Schema", StringComparison.Ordinal)) return ("SCHEMA01", ["PG"], "sql-concurrency");
        if (name.StartsWith("CW", StringComparison.Ordinal) || name.Contains("Crash", StringComparison.Ordinal) || name.Contains("Restart", StringComparison.Ordinal) || name.Contains("ResponseLoss", StringComparison.Ordinal)) return ("CW01", ["SH", "PG", "CW"], "restart|process-crash");
        if (name.StartsWith("OPT", StringComparison.Ordinal) || name.Contains("Optional", StringComparison.Ordinal)) return ("OPT01", ["HT", "DEL"], "semantic");
        if (name.StartsWith("MRC", StringComparison.Ordinal) || name.Contains("RequiredConsumer", StringComparison.Ordinal)) return ("MRC01", ["DEL", "HT"], "semantic");
        if (name.StartsWith("H", StringComparison.Ordinal) || name.Contains("HumanTask", StringComparison.Ordinal)) return ("H01", ["HT", "IM", "PG"], "semantic");
        if (name.StartsWith("W", StringComparison.Ordinal) || name.StartsWith("Workflow", StringComparison.Ordinal) || name.Contains("Accountability", StringComparison.Ordinal) || name.Contains("Audit", StringComparison.Ordinal)) return ("W01", ["WF", "ACCT", "PG"], "semantic");
        if (name.StartsWith("C", StringComparison.Ordinal) || name.Contains("Composition", StringComparison.Ordinal) || name.Contains("Contract", StringComparison.Ordinal)) return ("C01", ["SH", "IM", "PG"], "composition");
        if (name.StartsWith("L", StringComparison.Ordinal) || name.StartsWith("A", StringComparison.Ordinal) || name.StartsWith("R", StringComparison.Ordinal) || name.Contains("Outbox", StringComparison.Ordinal) || name.Contains("Claim", StringComparison.Ordinal) || name.Contains("Terminal", StringComparison.Ordinal)) return ("L01", ["SH", "IM", "PG"], "semantic");
        return ("A01", ["SH"], "semantic");
    }

    private static string CaseId(string name, string prefix, int max)
    {
        var ordinal = Phase9cAcceptanceManifest.NormativeNames.Concat(Phase9cSupplementalAcceptanceManifest.Names)
            .Where(item => item.StartsWith(prefix, StringComparison.Ordinal)).TakeWhile(item => !string.Equals(item, name, StringComparison.Ordinal)).Count() + 1;
        return $"{prefix}{Math.Min(ordinal, max):00}";
    }
}
