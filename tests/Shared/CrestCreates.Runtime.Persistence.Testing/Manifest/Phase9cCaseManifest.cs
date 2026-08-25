namespace CrestCreates.Runtime.Persistence.Testing.Manifest;

public sealed record Phase9cCase(string CaseId, string Slice, string RequiredRunner, string EvidenceVector);

public static class Phase9cCaseManifest
{
    public static IReadOnlyList<Phase9cCase> Cases { get; } =
        [
            .. Expand("A", 12, "2", "Shared", "semantic"),
            .. Expand("L", 17, "3", "InMemory|PostgreSql", "semantic"),
            .. Expand("R", 13, "4", "InMemory|PostgreSql|CrashWorker", "restart|process-crash"),
            .. Expand("H", 23, "5", "HumanTask|InMemory|PostgreSql", "semantic"),
            .. Expand("W", 14, "6", "Workflow|InMemory|PostgreSql", "semantic"),
            .. Expand("C", 15, "7", "Shared|InMemory|PostgreSql", "composition"),
            .. Expand("N", 9, "9", "PostgreSql|Aot", "native"),
            .. Expand("ARCH", 16, "1", "Boundary", "composition"),
            .. Expand("MRC", 4, "6", "Delivery|HumanTask|Procurement|Workflow", "semantic"),
            .. Expand("PROC", 7, "6", "Procurement", "semantic"),
            .. Expand("RCA", 2, "6", "Workflow|InMemory|PostgreSql", "semantic"),
            .. Expand("BOOT", 3, "7", "Delivery|PostgreSql|Boundary", "composition"),
            .. Expand("SCHEMA", 2, "7", "PostgreSql", "sql-concurrency"),
            .. Expand("OPT", 2, "5", "HumanTask|Delivery", "semantic"),
            new("HOC01", "5", "HumanTask|Delivery", "composition"),
            new("ACT01", "7", "AgentControlPlane", "semantic"), new("ACT02", "7", "AgentControlPlane", "semantic"),
            new("OUT01", "7", "HumanTask", "semantic"), new("OUT02", "7", "HumanTask|Workflow", "semantic"),
            new("CW01", "9", "CrashWorker|PostgreSql", "process-crash"), new("CW02", "9", "CrashWorker|PostgreSql", "process-crash"), new("CW03", "9", "CrashWorker|PostgreSql", "process-crash"), new("CW04", "9", "CrashWorker|PostgreSql", "process-crash"), new("CW04B", "9", "CrashWorker|PostgreSql", "process-crash"), new("CW05", "9", "CrashWorker|PostgreSql", "process-crash"), new("CW06", "9", "CrashWorker|PostgreSql", "process-crash"), new("CW07", "9", "CrashWorker|PostgreSql", "process-crash")
        ];

    private static IEnumerable<Phase9cCase> Expand(string prefix, int count, string slice, string runner, string vector)
        => Enumerable.Range(1, count).Select(index => new Phase9cCase($"{prefix}{index:00}", slice, runner, vector));
}
