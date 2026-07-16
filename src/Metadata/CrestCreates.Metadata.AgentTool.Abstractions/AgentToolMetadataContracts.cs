namespace CrestCreates.Metadata.AgentTool;

public enum AgentToolSelectionPolicy
{
    Unknown = 0,
    ExplicitOnly = 1,
    AutomaticAllowed = 2
}

public enum AgentToolSideEffectKind
{
    Unknown = 0,
    ReadOnly = 1,
    InternalWrite = 2,
    ExternalWrite = 3,
    Destructive = 4
}

public enum AgentToolApprovalMode
{
    Unknown = 0,
    PolicyDriven = 1,
    Required = 2,
    None = 3
}

public enum AgentToolAuditMode
{
    Unknown = 0,
    Required = 1,
    BestEffort = 2
}

public sealed record AgentToolBudgetRequirement
{
    public required string Category { get; init; }

    public long CostUnits { get; init; } = 1;

    public int? MaxCallsPerExecution { get; init; }
}
