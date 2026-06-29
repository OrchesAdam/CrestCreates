using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.HumanTask.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// Default implementation of IActivationReviewOrchestrator.
/// Creates HumanTask instances for activation review and processes
/// completed review decisions by routing them to the activation request service.
/// </summary>
public sealed class DefaultActivationReviewOrchestrator : IActivationReviewOrchestrator
{
    private readonly IHumanTaskRuntime _humanTaskRuntime;
    private readonly IDescriptorActivationRequestService _activationRequestService;
    private readonly ILogger<DefaultActivationReviewOrchestrator> _logger;

    public DefaultActivationReviewOrchestrator(
        IHumanTaskRuntime humanTaskRuntime,
        IDescriptorActivationRequestService activationRequestService,
        ILogger<DefaultActivationReviewOrchestrator> logger)
    {
        _humanTaskRuntime = humanTaskRuntime;
        _activationRequestService = activationRequestService;
        _logger = logger;
    }

    public async Task<AgentToolResult<string>> CreateActivationReviewTaskAsync(
        AgentToolInvocationContext context, ActivationRequest activationRequest,
        DescriptorActivationPolicy policy, CancellationToken ct = default)
    {
        if (activationRequest.Eligibility != DescriptorActivationEligibility.RequiresHumanReview)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.ReviewNotRequired,
                Severity = SeverityLevel.Error,
                Message = $"Activation review task not required for eligibility '{activationRequest.Eligibility}'."
            };
            return AgentToolResult<string>.InvalidRequest([diag]);
        }

        if (activationRequest.BindingSnapshot?.Hashes is null)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.BindingHashesRequired,
                Severity = SeverityLevel.Error,
                Message = "Cannot create review task: BindingSnapshot.Hashes is required but was null. " +
                          "This indicates a malformed activation request."
            };
            return AgentToolResult<string>.InvalidRequest([diag]);
        }

        var taskInput = new DescriptorActivationReviewTaskInput
        {
            ActivationRequestId = activationRequest.RequestId,
            DraftId = activationRequest.DraftId,
            TenantId = activationRequest.TenantId,
            Eligibility = activationRequest.Eligibility,
            GovernanceDecision = activationRequest.GovernanceDecision.ToString(),
            PolicySummary = $"ForbidSelfApproval={policy.ForbidSelfApproval}, RequireHumanReviewForAll={policy.RequireHumanReviewForAll}",
            Rationale = null,
            CorrelationId = activationRequest.CorrelationId,
            ReviewSummary = $"ReviewResultId={activationRequest.BindingSnapshot.ReviewResultId}, " +
                $"Governance={activationRequest.GovernanceDecision}",
            EvidenceSummary = activationRequest.BindingSnapshot.Hashes is not null
                ? $"ContractHash={activationRequest.BindingSnapshot.Hashes.ContractHash.Value[..Math.Min(16, activationRequest.BindingSnapshot.Hashes.ContractHash.Value.Length)]}..., " +
                  $"DefinitionHash={activationRequest.BindingSnapshot.Hashes.DefinitionHash.Value[..Math.Min(16, activationRequest.BindingSnapshot.Hashes.DefinitionHash.Value.Length)]}..."
                : null,
            BoundHashes = activationRequest.BindingSnapshot.Hashes,
            PackageManifestSummary = activationRequest.BindingSnapshot.Hashes is not null
                ? $"PackageManifestHash={activationRequest.BindingSnapshot.Hashes.PackageManifestHash}, PackageEvidenceHash={activationRequest.BindingSnapshot.Hashes.PackageEvidenceHash}"
                : null,
            ImpactContext = activationRequest.GovernanceDecision.ToString()
        };

        var creationRequest = new HumanTaskCreationRequest
        {
            HumanTaskId = DescriptorActivationHumanTaskIds.ActivationReview,
            TenantId = activationRequest.TenantId,
            Input = taskInput,
            WorkflowInstanceId = null,
            WorkflowStepId = null
        };

        var instance = await _humanTaskRuntime.CreateAsync(creationRequest, ct);

        _logger.LogInformation(
            "Created activation review HumanTask {TaskInstanceId} for activation request {RequestId}",
            instance.Id, activationRequest.RequestId);

        return AgentToolResult<string>.Success(instance.Id);
    }

    public async Task ProcessReviewDecisionAsync(
        DescriptorActivationReviewDecision reviewDecision, CancellationToken ct = default)
    {
        var context = new AgentToolInvocationContext
        {
            ActorId = reviewDecision.ActorId,
            ActorKind = reviewDecision.ActorKind switch
            {
                DescriptorActivationActorKind.Human => AgentToolActorKind.Human,
                DescriptorActivationActorKind.Agent => AgentToolActorKind.Agent,
                _ => AgentToolActorKind.System
            },
            TenantId = reviewDecision.TenantId,
            CorrelationId = reviewDecision.CorrelationId,
            InvocationSource = AgentToolInvocationSource.HumanTaskCallback,
            ToolName = string.Empty
        };

        if (reviewDecision.Decision == DescriptorActivationReviewOutcome.Approved)
        {
            _logger.LogInformation(
                "Processing approval for activation request {RequestId} by actor {ActorId}",
                reviewDecision.ActivationRequestId, reviewDecision.ActorId);

            // ApproveActivationRequestAsync now internally executes evidence recheck + gate
            await _activationRequestService.ApproveActivationRequestAsync(
                context, reviewDecision.ActivationRequestId, reviewDecision, ct);
        }
        else if (reviewDecision.Decision == DescriptorActivationReviewOutcome.Rejected)
        {
            _logger.LogInformation(
                "Processing rejection for activation request {RequestId} by actor {ActorId}",
                reviewDecision.ActivationRequestId, reviewDecision.ActorId);

            await _activationRequestService.RejectActivationRequestAsync(
                context, reviewDecision.ActivationRequestId, reviewDecision, ct);
        }
    }
}
