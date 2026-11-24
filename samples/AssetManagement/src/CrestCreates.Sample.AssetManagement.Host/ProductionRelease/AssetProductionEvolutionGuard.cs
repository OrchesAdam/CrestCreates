using System.Globalization;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

internal static class AssetProductionEvolutionGuard
{
    internal const string DiagnosticCode = "ASSET_PRODUCTION_EVOLUTION_INVALID";
    internal static string BaseVersion => AssetDescriptorCatalog.MaintenanceWorkflow.Version.ToString(CultureInfo.InvariantCulture);
    internal static string ProposedVersion => AssetProductionDescriptors.WorkflowVersion.ToString(CultureInfo.InvariantCulture);

    internal static bool TargetsProductionWorkflow(Draft draft) =>
        (draft.DescriptorId == AssetProductionDescriptors.WorkflowId &&
            int.TryParse(draft.ProposedVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) &&
            version == AssetProductionDescriptors.WorkflowVersion) ||
        draft.Payload?.GetDescriptor() is WorkflowDescriptor
        {
            Id: AssetProductionDescriptors.WorkflowId, Version: AssetProductionDescriptors.WorkflowVersion
        };

    internal static WorkflowDescriptor RequireDraft(Draft draft, string tenantId)
    {
        if (draft.TenantId != tenantId || draft.DescriptorId != AssetProductionDescriptors.WorkflowId ||
            draft.DescriptorKind != DescriptorKind.Workflow || draft.Operation != DescriptorDraftOperation.Update ||
            draft.BaseVersion != BaseVersion || draft.ProposedVersion != ProposedVersion ||
            draft.Payload?.GetDescriptor() is not WorkflowDescriptor workflow)
        {
            throw new InvalidOperationException("Asset production evolution requires one update of the compiled maintenance workflow to version 3 in the current tenant.");
        }

        AssetProductionDescriptors.RequireProductionWorkflow(workflow);
        if (workflow.VariableSchema is not null || workflow.SupersededById is not null ||
            workflow.DefaultVariableScope != WorkflowVariableScope.Workflow ||
            workflow.Steps.Any(step => step.OnError != StepErrorBehavior.Fail || step.Transitions is not { Count: 0 } ||
                step.InputMapping is not null || step.OutputMapping is not null))
        {
            throw new InvalidOperationException("Asset production workflow steps require default variable scope and fail-on-error behavior without custom mappings or transitions.");
        }

        return workflow;
    }
}
