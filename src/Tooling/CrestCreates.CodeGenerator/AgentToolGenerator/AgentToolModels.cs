using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.AgentToolGenerator;

internal sealed class AgentToolSpecModel
{
    public string SpecName { get; set; } = string.Empty;
    public string CapabilityId { get; set; } = string.Empty;
    public int CapabilityVersion { get; set; }
    public string? ExpectedCapabilityContractHash { get; set; }
    public string DescriptorId { get; set; } = string.Empty;
    public int DescriptorVersion { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? InputType { get; set; }
    public string? OutputType { get; set; }
    public int SelectionPolicy { get; set; }
    public int SideEffectKind { get; set; }
    public int RiskFloor { get; set; }
    public int ApprovalMode { get; set; }
    public string BudgetCategory { get; set; } = string.Empty;
    public long CostUnits { get; set; }
    public int MaxCallsPerExecution { get; set; }
    public int AuditMode { get; set; }
    public ImmutableArray<string> AllowedAgentRoles { get; set; }
}

internal sealed class AgentToolContainerModel
{
    public string Namespace { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ImmutableArray<AgentToolSpecModel> Specs { get; set; }
    public ImmutableArray<Diagnostic> Diagnostics { get; set; }
    public bool GenerateDescriptorProviderRegistration { get; set; } = true;
}
