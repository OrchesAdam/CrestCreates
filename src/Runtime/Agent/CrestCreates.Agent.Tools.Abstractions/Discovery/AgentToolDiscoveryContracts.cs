using System.Text.Json;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools;

public sealed record AgentToolContractIdentity(
    string Id,
    int Version,
    string ContractHash);

public sealed record AgentToolSchemaContractIdentity(
    string Id,
    int Version,
    string ContractHash);

public sealed record AgentToolEffectiveGovernance(
    AgentToolSelectionPolicy SelectionPolicy,
    AgentToolSideEffectKind SideEffectKind,
    CapabilityRiskLevel EffectiveRisk,
    AgentToolApprovalMode EffectiveApprovalMode,
    AgentToolBudgetRequirement Budget,
    AgentToolAuditMode EffectiveAuditMode);

public sealed record AgentToolDiscoveryContract
{
    public required string ToolName { get; init; }

    public string? Title { get; init; }

    public required string Description { get; init; }

    public required JsonElement InputSchema { get; init; }

    public JsonElement? OutputSchema { get; init; }

    public required AgentToolContractIdentity ToolContract { get; init; }

    public required AgentToolContractIdentity CapabilityContract { get; init; }

    public AgentToolSchemaContractIdentity? InputSchemaContract { get; init; }

    public AgentToolSchemaContractIdentity? OutputSchemaContract { get; init; }

    public required AgentToolEffectiveGovernance Governance { get; init; }
}

public interface IAgentToolCatalog
{
    ValueTask<IReadOnlyList<AgentToolDiscoveryContract>> ListAsync(
        CancellationToken cancellationToken = default);
}
