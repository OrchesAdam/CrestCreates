namespace CrestCreates.Runtime.Persistence.Testing.Manifest;

/// <summary>
/// Maps every frozen acceptance to the test runner that owns its executable
/// evidence.  The mapping is intentionally coarse-grained: a runner is a
/// complete test project/slice, while the exact acceptance names remain
/// frozen in the two acceptance manifests.
/// </summary>
public static class Phase9cEvidenceRunnerCatalog
{
    public static IReadOnlySet<string> RunnerNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "delivery", "dispatch", "recovery", "workflow", "accountability",
        "procurement", "activation", "boundary", "aot"
    };

    public static string RunnerFor(string acceptanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(acceptanceName);

        if (acceptanceName.StartsWith("ARCH", StringComparison.Ordinal)
            || acceptanceName.Contains("Composition", StringComparison.Ordinal)
            || acceptanceName.Contains("Reference", StringComparison.Ordinal)
            || acceptanceName.Contains("Boundary", StringComparison.Ordinal))
            return "boundary";
        if (acceptanceName.StartsWith("N", StringComparison.Ordinal)
            || acceptanceName.StartsWith("V012", StringComparison.Ordinal)
            || acceptanceName.Contains("NativeAot", StringComparison.OrdinalIgnoreCase)
            || acceptanceName.Contains("NativeBinary", StringComparison.Ordinal))
            return "aot";
        if (acceptanceName.StartsWith("PROC", StringComparison.Ordinal)
            || acceptanceName.Contains("Procurement", StringComparison.Ordinal))
            return "procurement";
        if (acceptanceName.StartsWith("ACT", StringComparison.Ordinal)
            || acceptanceName.StartsWith("Activation", StringComparison.Ordinal))
            return "activation";
        if (acceptanceName.Contains("Accountability", StringComparison.Ordinal)
            || acceptanceName.StartsWith("Workflow_", StringComparison.Ordinal)
            || acceptanceName.Contains("Audit", StringComparison.Ordinal))
            return "accountability";
        if (acceptanceName.StartsWith("H", StringComparison.Ordinal)
            || acceptanceName.StartsWith("OPT", StringComparison.Ordinal)
            || acceptanceName.StartsWith("MRC", StringComparison.Ordinal)
            || acceptanceName.StartsWith("OUT", StringComparison.Ordinal)
            || acceptanceName.StartsWith("HOC", StringComparison.Ordinal)
            || acceptanceName.Contains("HumanTask", StringComparison.Ordinal)
            || acceptanceName.Contains("Continuation", StringComparison.Ordinal))
            return "workflow";
        if (acceptanceName.StartsWith("R", StringComparison.Ordinal)
            || acceptanceName.StartsWith("CW", StringComparison.Ordinal)
            || acceptanceName.Contains("Restart", StringComparison.Ordinal)
            || acceptanceName.Contains("Crash", StringComparison.Ordinal)
            || acceptanceName.Contains("Lease", StringComparison.Ordinal))
            return "recovery";
        if (acceptanceName.StartsWith("L", StringComparison.Ordinal)
            || acceptanceName.StartsWith("A", StringComparison.Ordinal)
            || acceptanceName.StartsWith("C", StringComparison.Ordinal)
            || acceptanceName.StartsWith("RCA", StringComparison.Ordinal)
            || acceptanceName.Contains("Outbox", StringComparison.Ordinal)
            || acceptanceName.Contains("Claim", StringComparison.Ordinal)
            || acceptanceName.Contains("Terminal", StringComparison.Ordinal))
            return "dispatch";
        return "delivery";
    }

    public static string EvidenceVectorFor(string runner)
        => runner switch
        {
            "recovery" => "restart|process-crash",
            "aot" => "native",
            "boundary" => "composition",
            _ => "semantic"
        };
}
