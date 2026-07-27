using CrestCreates.Agent.Tools;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Memory.Tools;

[AgentToolSpecs(GenerateDescriptorProviderRegistration = false)]
public static partial class AgentMemoryToolSpecifications
{
    [AgentToolSpec(AgentMemoryToolCapabilityIds.BuildPack, DescriptorId = "agent-tool:agent.memory.build-pack", CapabilityVersion = 1,
        InputType = typeof(BuildAgentMemoryPackInput), OutputType = typeof(BuildAgentMemoryPackResult),
        ToolName = AgentMemoryToolCapabilityIds.BuildPack, Title = "Build agent memory pack",
        Description = "Builds a governed, visibility-filtered memory pack.", SelectionPolicy = AgentToolSelectionPolicy.AutomaticAllowed,
        SideEffectKind = AgentToolSideEffectKind.ReadOnly, RiskFloor = AgentToolRiskFloor.Low,
        ApprovalMode = AgentToolApprovalMode.None, BudgetCategory = "agent-memory-recall", CostUnits = 1,
        MaxCallsPerExecution = 16, AuditMode = AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { "memory-reader", "memory-processor", "memory-curator" })]
    public sealed class BuildPack;

    [AgentToolSpec(AgentMemoryToolCapabilityIds.ExpandSource, DescriptorId = "agent-tool:agent.memory.expand-source", CapabilityVersion = 1,
        InputType = typeof(ExpandAgentMemorySourceInput), OutputType = typeof(ExpandAgentMemorySourceResult),
        ToolName = AgentMemoryToolCapabilityIds.ExpandSource, Title = "Expand agent memory source",
        Description = "Expands one governed memory source grant.", SelectionPolicy = AgentToolSelectionPolicy.ExplicitOnly,
        SideEffectKind = AgentToolSideEffectKind.ReadOnly, RiskFloor = AgentToolRiskFloor.Medium,
        ApprovalMode = AgentToolApprovalMode.PolicyDriven, BudgetCategory = "agent-memory-expand", CostUnits = 1,
        MaxCallsPerExecution = 16, AuditMode = AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { "memory-reader", "memory-processor", "memory-curator" })]
    public sealed class ExpandSource;

    [AgentToolSpec(AgentMemoryToolCapabilityIds.CompressHistory, DescriptorId = "agent-tool:agent.memory.compress-history", CapabilityVersion = 1,
        InputType = typeof(CompressAgentHistoryInput), OutputType = typeof(CompressAgentHistoryResult),
        ToolName = AgentMemoryToolCapabilityIds.CompressHistory, Title = "Compress agent history",
        Description = "Compresses one authorized conversation or task history.", SelectionPolicy = AgentToolSelectionPolicy.AutomaticAllowed,
        SideEffectKind = AgentToolSideEffectKind.InternalWrite, RiskFloor = AgentToolRiskFloor.Medium,
        ApprovalMode = AgentToolApprovalMode.PolicyDriven, BudgetCategory = "agent-memory-process", CostUnits = 2,
        MaxCallsPerExecution = 8, AuditMode = AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { "memory-processor", "memory-curator" })]
    public sealed class CompressHistory;

    [AgentToolSpec(AgentMemoryToolCapabilityIds.ExtractCandidates, DescriptorId = "agent-tool:agent.memory.extract-candidates", CapabilityVersion = 1,
        InputType = typeof(ExtractMemoryCandidatesInput), OutputType = typeof(ExtractMemoryCandidatesResult),
        ToolName = AgentMemoryToolCapabilityIds.ExtractCandidates, Title = "Extract memory candidates",
        Description = "Extracts governed candidates from one compressed context.", SelectionPolicy = AgentToolSelectionPolicy.AutomaticAllowed,
        SideEffectKind = AgentToolSideEffectKind.InternalWrite, RiskFloor = AgentToolRiskFloor.Medium,
        ApprovalMode = AgentToolApprovalMode.PolicyDriven, BudgetCategory = "agent-memory-process", CostUnits = 2,
        MaxCallsPerExecution = 8, AuditMode = AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { "memory-processor", "memory-curator" })]
    public sealed class ExtractCandidates;

    [AgentToolSpec(AgentMemoryToolCapabilityIds.PromoteCandidate, DescriptorId = "agent-tool:agent.memory.promote-candidate", CapabilityVersion = 1,
        InputType = typeof(PromoteMemoryCandidateInput), OutputType = typeof(PromoteMemoryCandidateResult),
        ToolName = AgentMemoryToolCapabilityIds.PromoteCandidate, Title = "Promote memory candidate",
        Description = "Promotes one visible candidate through governed curation.", SelectionPolicy = AgentToolSelectionPolicy.ExplicitOnly,
        SideEffectKind = AgentToolSideEffectKind.InternalWrite, RiskFloor = AgentToolRiskFloor.Medium,
        ApprovalMode = AgentToolApprovalMode.PolicyDriven, BudgetCategory = "agent-memory-curation", CostUnits = 2,
        MaxCallsPerExecution = 8, AuditMode = AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { "memory-curator" })]
    public sealed class PromoteCandidate;

    [AgentToolSpec(AgentMemoryToolCapabilityIds.RejectCandidate, DescriptorId = "agent-tool:agent.memory.reject-candidate", CapabilityVersion = 1,
        InputType = typeof(RejectMemoryCandidateInput), OutputType = typeof(RejectMemoryCandidateResult),
        ToolName = AgentMemoryToolCapabilityIds.RejectCandidate, Title = "Reject memory candidate",
        Description = "Rejects one visible candidate through governed curation.", SelectionPolicy = AgentToolSelectionPolicy.ExplicitOnly,
        SideEffectKind = AgentToolSideEffectKind.InternalWrite, RiskFloor = AgentToolRiskFloor.Medium,
        ApprovalMode = AgentToolApprovalMode.PolicyDriven, BudgetCategory = "agent-memory-curation", CostUnits = 1,
        MaxCallsPerExecution = 8, AuditMode = AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { "memory-curator" })]
    public sealed class RejectCandidate;

    [AgentToolSpec(AgentMemoryToolCapabilityIds.SupersedeItem, DescriptorId = "agent-tool:agent.memory.supersede-item", CapabilityVersion = 1,
        InputType = typeof(SupersedeMemoryItemInput), OutputType = typeof(SupersedeMemoryItemResult),
        ToolName = AgentMemoryToolCapabilityIds.SupersedeItem, Title = "Supersede memory item",
        Description = "Supersedes one visible active memory with a candidate.", SelectionPolicy = AgentToolSelectionPolicy.ExplicitOnly,
        SideEffectKind = AgentToolSideEffectKind.InternalWrite, RiskFloor = AgentToolRiskFloor.High,
        ApprovalMode = AgentToolApprovalMode.Required, BudgetCategory = "agent-memory-curation", CostUnits = 3,
        MaxCallsPerExecution = 4, AuditMode = AgentToolAuditMode.Required,
        AllowedAgentRoles = new[] { "memory-curator" })]
    public sealed class SupersedeItem;
}
