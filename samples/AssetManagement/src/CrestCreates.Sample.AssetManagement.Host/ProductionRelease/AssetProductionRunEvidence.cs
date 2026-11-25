using CrestCreates.Agent.Orchestration;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

public sealed class AssetProductionRunEvidence
{
    private const string RunVariable = "productionEvolutionRunId";
    private readonly AssetProductionRelease? _release;

    internal AssetProductionRunEvidence(AssetProductionRelease? release) => _release = release;

    internal void Bind(string tenantId, int? version, Dictionary<string, RuntimeStateValue> input,
        IRuntimeStateContractRegistry state)
    {
        if (version == AssetProductionDescriptors.WorkflowVersion && _release?.TenantId == tenantId &&
            Guid.TryParseExact(_release.WorkflowApproval.Binding.CorrelationId, "N", out var runId) && runId != Guid.Empty)
        {
            input.Add(RunVariable, state.Capture(runId));
        }
    }

    internal static async Task RecordReleaseAsync(WorkflowInstance workflow, string executionId, Guid operationId,
        IRuntimeStateContractRegistry state, IAgentExecutionStore executionStore, CancellationToken ct)
    {
        if (!workflow.Variables.TryGetValue(RunVariable, out var encoded) || state.Restore(encoded) is not Guid runId)
            return;

        var coordinator = new AgentRunCoordinator(executionStore, TimeProvider.System);
        var key = new AgentExecutionKey(workflow.TenantId, runId);
        await coordinator.RecordWorkflowFactAsync(key, new AgentWorkflowFact(
            workflow.InstanceId, workflow.WorkflowPin.Ref.Id, workflow.WorkflowPin.Ref.Version!.Value,
            workflow.WorkflowPin.DefinitionHash.Value, AgentWorkflowOutcome.Completed), ct);
        await coordinator.RecordCapabilityFactAsync(key, new AgentCapabilityFact(
            executionId, AssetProductionDescriptors.ReleaseCapabilityId,
            AgentCapabilityCommit.Committed, operationId.ToString("N")), ct);
    }
}
