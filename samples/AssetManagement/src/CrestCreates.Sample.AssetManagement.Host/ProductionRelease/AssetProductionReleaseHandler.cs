using System.Security.Cryptography;
using CrestCreates.Agent.Orchestration;
using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Sample.AssetManagement.Application;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Mes;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

internal sealed class AssetProductionReleaseHandler : ICapabilityContextAwareHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct) =>
        throw new InvalidOperationException("A capability context is required.");

    public async Task<object?> InvokeAsync(CapabilityExecutionContext context, CancellationToken ct)
    {
        if (context.Input is not AssetProductionReleaseInput input || context.TenantId is not { Length: > 0 } tenantId)
        {
            throw new CapabilityFailureException("ASSET_PRODUCTION_RELEASE_CONTEXT_INVALID", "The production release requires its typed input and tenant.");
        }

        var assetApplication = context.ServiceProvider.GetRequiredService<AssetApplicationService>();
        var workflows = context.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
        var state = context.ServiceProvider.GetRequiredService<IRuntimeStateContractRegistry>();
        var assetStore = context.ServiceProvider.GetRequiredService<IAssetStore>();
        var mes = context.ServiceProvider.GetRequiredService<AssetMesClient>();
        var workflow = await workflows.GetAsync(new RuntimeInstanceKey(tenantId, input.WorkflowInstanceId), ct);
        if (workflow is null || workflow.Status != WorkflowInstanceStatus.Completed ||
            workflow.WorkflowPin.Ref.Id != AssetContractIds.MaintenanceWorkflow ||
            workflow.WorkflowPin.Ref.Version != AssetProductionDescriptors.WorkflowVersion ||
            !workflow.Variables.TryGetValue("assetId", out var encodedAsset) ||
            state.Restore(encodedAsset) is not Guid assetId || assetId != input.AssetId)
        {
            throw new CapabilityFailureException("ASSET_PRODUCTION_RELEASE_WORKFLOW_INVALID", "The production release requires a completed approved critical maintenance workflow for this asset.");
        }

        _ = await assetApplication.GetAsync(input.AssetId, tenantId, ct)
            ?? throw new CapabilityFailureException("ASSET_NOT_VISIBLE", "The production asset is outside the caller's data scope.");
        var asset = await assetStore.GetAsync(tenantId, input.AssetId, ct);
        if (asset is null || asset.Category != AssetProductionDescriptors.CriticalCategory ||
            asset.Status is not (Domain.AssetStatus.Available or Domain.AssetStatus.Assigned))
        {
            throw new CapabilityFailureException("ASSET_PRODUCTION_RELEASE_ASSET_INVALID", "The asset is not ready for production release.");
        }

        var initial = workflow.StepResults.LastOrDefault(result => result.StepId == AssetProductionDescriptors.InitialStepId && result.HumanTaskOutcome is not null);
        var trialStep = workflow.StepResults.LastOrDefault(result => result.StepId == AssetProductionDescriptors.TrialStepId && result.HumanTaskOutcome is not null);
        var quality = workflow.StepResults.LastOrDefault(result => result.StepId == AssetProductionDescriptors.QualityStepId && result.HumanTaskOutcome is not null);
        if (initial?.HumanTaskOutcome != CompletionCondition.Approve ||
            trialStep?.HumanTaskOutcome != CompletionCondition.Approve ||
            quality?.HumanTaskOutcome != CompletionCondition.Approve ||
            initial.Output is null || trialStep.Output is null || quality.Output is null ||
            state.Restore(initial.Output) is not AssetMaintenanceEvidenceFact supervisor || !supervisor.Approved ||
            state.Restore(trialStep.Output) is not AssetMaintenanceEvidenceFact trial || !trial.Approved ||
            state.Restore(quality.Output) is not AssetMaintenanceEvidenceFact signOff || !signOff.Approved ||
            supervisor.ActorId == signOff.ActorId)
        {
            throw new CapabilityFailureException("ASSET_PRODUCTION_RELEASE_APPROVALS_INVALID", "The production release requires supervisor, MES and independent quality approvals.");
        }

        var observation = trial.Observation;
        if (observation is null || observation.Outcome != TrialOutcome.Passed ||
            observation.TenantId != tenantId || observation.EquipmentId != input.AssetId ||
            observation.WorkOrderId != input.WorkflowInstanceId || observation.ObservationId != input.ObservationId ||
            observation.SourceEventId != input.SourceEventId)
        {
            throw new CapabilityFailureException("ASSET_MES_EVIDENCE_REJECTED", "The release evidence does not match the completed workflow.");
        }

        var operationId = OperationId(tenantId, input.WorkflowInstanceId);
        var existing = await mes.FindReleaseAsync(tenantId, input.AssetId, input.WorkflowInstanceId, operationId, ct);
        if (existing is not null)
        {
            return await ConfirmReceiptAsync(existing, operationId, observation, workflow, state, context, ct);
        }

        var command = new ReleaseProductionLockRequest(operationId, observation.ObservationId, observation.SourceEventId);
        ProductionReleaseReceipt receipt;
        try
        {
            receipt = await mes.ReleaseAsync(tenantId, input.AssetId, input.WorkflowInstanceId, command, ct);
        }
        catch (CapabilityFailureException exception) when (exception.ErrorCode == "ASSET_MES_RELEASE_UNCONFIRMED")
        {
            var recovered = await mes.FindReleaseAsync(tenantId, input.AssetId, input.WorkflowInstanceId, operationId, ct);
            if (recovered is null)
            {
                throw;
            }

            receipt = recovered;
        }

        return await ConfirmReceiptAsync(receipt, operationId, observation, workflow, state, context, ct);
    }

    private static async Task<AssetProductionReleaseFact> ConfirmReceiptAsync(ProductionReleaseReceipt receipt,
        Guid operationId, TrialObservation observation, WorkflowInstance workflow, IRuntimeStateContractRegistry state,
        CapabilityExecutionContext context, CancellationToken ct)
    {
        if (receipt.OperationId != operationId || receipt.Evidence != observation)
        {
            throw new CapabilityFailureException("ASSET_MES_RECEIPT_INVALID", "The MES release receipt does not match the approved evidence.");
        }

        await AssetProductionRunEvidence.RecordReleaseAsync(workflow, context.ExecutionId!, operationId, state,
            context.ServiceProvider.GetRequiredService<IAgentExecutionStore>(), ct);
        return new AssetProductionReleaseFact { OperationId = operationId, Receipt = receipt };
    }

    internal static Guid OperationId(string tenantId, string workflowInstanceId)
    {
        var material = System.Text.Encoding.UTF8.GetBytes($"asset-production-release\0{tenantId}\0{workflowInstanceId}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(material, hash);
        return new Guid(hash[..16]);
    }
}

internal sealed class AssetProductionCapabilityModule : ICapabilityHandlerModule
{
    public string Id => "asset-production-release";

    public void Apply(CapabilityHandlerResolver resolver)
    {
        resolver.Register(AssetProductionDescriptors.ReleaseCapabilityId, new AssetProductionReleaseHandler());
        resolver.Register(AssetContractIds.CompleteMaintenanceTaskCapability, new AssetMaintenanceTaskHandler());
    }
}
