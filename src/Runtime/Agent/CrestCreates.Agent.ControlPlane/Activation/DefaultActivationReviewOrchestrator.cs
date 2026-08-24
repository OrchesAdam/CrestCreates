using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// Default implementation of IActivationReviewOrchestrator.
/// Creates HumanTask instances for activation review and processes
/// completed review decisions by routing them to the activation request service.
/// </summary>
public sealed class DefaultActivationReviewOrchestrator : IActivationReviewOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDescriptorActivationRequestService _activationRequestService;
    private readonly ILogger<DefaultActivationReviewOrchestrator> _logger;
    private readonly IRuntimeStateContractRegistry _stateRegistry;

    public DefaultActivationReviewOrchestrator(
        IServiceScopeFactory scopeFactory,
        IDescriptorActivationRequestService activationRequestService,
        ILogger<DefaultActivationReviewOrchestrator> logger,
        IRuntimeStateContractRegistry stateRegistry)
    {
        _scopeFactory = scopeFactory;
        _activationRequestService = activationRequestService;
        _logger = logger;
        _stateRegistry = stateRegistry;
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
            Input = _stateRegistry.Capture(taskInput),
            WorkflowKey = null,
            WorkflowStepId = null,
            RequiredCompletionConsumerIds = [DescriptorActivationReviewHumanTaskEventHandler.ConsumerIdValue]
        };

        // HumanTask runtime is scoped because it owns the scoped local-event/transaction
        // collaborators. The control-plane orchestrator remains singleton, so acquire the
        // runtime only for the operation that creates the task and dispose that scope after
        // the transactional call completes.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var instance = await scope.ServiceProvider
            .GetRequiredService<IHumanTaskRuntime>()
            .CreateAsync(creationRequest, ct);

        _logger.LogInformation(
            "Created activation review HumanTask {TaskInstanceId} for activation request {RequestId}",
            instance.Id, activationRequest.RequestId);

        return AgentToolResult<string>.Success(instance.Id);
    }

    public async Task<ActivationReviewDispatchOutcome> ProcessReviewDecisionAsync(
        DescriptorActivationReviewDecision reviewDecision,
        string completionEventId,
        CancellationToken ct = default)
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
            var result = await _activationRequestService.ApproveActivationRequestAsync(
                context, reviewDecision.ActivationRequestId, reviewDecision, ct, completionEventId);
            return Classify(result);
        }
        else if (reviewDecision.Decision == DescriptorActivationReviewOutcome.Rejected)
        {
            _logger.LogInformation(
                "Processing rejection for activation request {RequestId} by actor {ActorId}",
                reviewDecision.ActivationRequestId, reviewDecision.ActorId);

            var result = await _activationRequestService.RejectActivationRequestAsync(
                context, reviewDecision.ActivationRequestId, reviewDecision, ct, completionEventId);
            return Classify(result);
        }

        return ActivationReviewDispatchOutcome.Conflict;
    }

    private static ActivationReviewDispatchOutcome Classify(AgentToolResult<ActivationRequest> result)
    {
        if (result.Status == AgentToolResultStatus.Success)
            return ActivationReviewDispatchOutcome.Accepted;
        if (result.Status == AgentToolResultStatus.SucceededWithDiagnostics
            && result.Diagnostics.Any(d => string.Equals(d.Code.Value, DescriptorActivationDiagnosticCodes.ReviewDuplicate.Value, StringComparison.Ordinal)))
            return ActivationReviewDispatchOutcome.Duplicate;
        return ActivationReviewDispatchOutcome.Conflict;
    }
}
