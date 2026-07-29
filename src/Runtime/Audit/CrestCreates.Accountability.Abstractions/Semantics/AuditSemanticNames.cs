namespace CrestCreates.Accountability.Abstractions.Semantics;

public static class AuditActorKinds
{
    public const string User = "user";
    public const string Anonymous = "anonymous";
    public const string System = "system";
    public const string Workflow = "workflow";
    public const string HumanTask = "human-task";
    public const string Agent = "agent";
    public const string Integration = "integration";
    public const string Scheduler = "scheduler";
    public const string McpClient = "mcp-client";
    public const string Unknown = "unknown";
}

public static class AuditActionKinds
{
    public const string HttpRequest = "http.request";
    public const string MethodInvoke = "method.invoke";
    public const string CapabilityExecute = "capability.execute";
    public const string WorkflowLifecycle = "workflow.lifecycle";
}

public static class AuditOutcomeStatuses
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Skipped = "skipped";
    public const string Indeterminate = "indeterminate";

    public static bool IsKnown(string? value)
        => value is Succeeded or Failed or Rejected or Cancelled or Skipped or Indeterminate;
}

public static class AuditInvocationSources
{
    public const string Http = "http";
    public const string Workflow = "workflow";
    public const string HumanTask = "human-task";
    public const string Agent = "agent";
    public const string Mcp = "mcp";
    public const string Integration = "integration";
    public const string System = "system";
}

public static class AuditSemanticNames
{
    public static bool IsStableKind(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            return false;

        var expectSegmentStart = true;
        foreach (var character in value)
        {
            if (expectSegmentStart)
            {
                if (character is < 'a' or > 'z')
                    return false;
                expectSegmentStart = false;
                continue;
            }

            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
                continue;
            if (character is '.' or '-')
            {
                expectSegmentStart = true;
                continue;
            }
            return false;
        }

        return !expectSegmentStart;
    }
}
