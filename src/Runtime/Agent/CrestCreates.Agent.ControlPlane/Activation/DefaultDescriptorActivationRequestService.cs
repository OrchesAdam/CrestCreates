using System.Collections.Concurrent;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using Microsoft.Extensions.Logging;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// Default implementation of IDescriptorActivationRequestService.
/// Owns request lifecycle, policy routing, approval/rejection, evidence recheck.
/// Does NOT own Workflow/HumanTask orchestration (separate responsibility).
/// Does NOT own runtime state mutation (IRuntimeActivationGate is the only executor).
/// </summary>
public class DefaultDescriptorActivationRequestService : IDescriptorActivationRequestService
{
    private readonly IDescriptorActivationPolicyProvider _policyProvider;
    private readonly IDescriptorActivationAuditor _auditor;
    private readonly IDescriptorStableHashBuilder _hashBuilder;
    private readonly DraftAbstractions.IDescriptorDraftStore _draftStore;
    private readonly IRuntimeActivationGate _activationGate;
    private readonly IActivationEvidenceRechecker _evidenceRechecker;
    private readonly ActivationBindingHashValidator _bindingHashValidator;
    private readonly ILogger<DefaultDescriptorActivationRequestService> _logger;

    private readonly ConcurrentDictionary<(string TenantId, string RequestId), ActivationResourceSnapshot> _requests = new();

    public DefaultDescriptorActivationRequestService(
        IDescriptorActivationPolicyProvider policyProvider,
        IDescriptorActivationAuditor auditor,
        IDescriptorStableHashBuilder hashBuilder,
        DraftAbstractions.IDescriptorDraftStore draftStore,
        IRuntimeActivationGate activationGate,
        IActivationEvidenceRechecker evidenceRechecker,
        ActivationBindingHashValidator bindingHashValidator,
        ILogger<DefaultDescriptorActivationRequestService> logger)
    {
        _policyProvider = policyProvider;
        _auditor = auditor;
        _hashBuilder = hashBuilder;
        _draftStore = draftStore;
        _activationGate = activationGate;
        _evidenceRechecker = evidenceRechecker;
        _bindingHashValidator = bindingHashValidator;
        _logger = logger;
    }

    public async Task<AgentToolResult<ActivationRequest>> CreateActivationRequestAsync(
        AgentToolInvocationContext context, SubmitActivationRequestRequest request, CancellationToken ct = default)
    {
        // Fail-closed: BindingSnapshot must be present (JSON/input-bound calls may bypass C# required constraints).
        if (request.BindingSnapshot is null)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.BindingSnapshotRequired,
                Severity = SeverityLevel.Error,
                Message = "BindingSnapshot is required for activation request submission."
            };
            await RecordAudit(context, null, DescriptorActivationAuditAction.Block, "MissingBindingSnapshot", [diag], ct);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        // Fail-closed: BindingSnapshot.Hashes must be present (JSON/input-bound calls may bypass C# required constraints).
        if (request.BindingSnapshot.Hashes is null)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.BindingHashesRequired,
                Severity = SeverityLevel.Error,
                Message = "BindingSnapshot.Hashes is required for activation request submission."
            };
            await RecordAudit(context, null, DescriptorActivationAuditAction.Block, "MissingBindingHashes", [diag], ct);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        // Validate binding hashes for completeness and metadata consistency
        var hashIssues = _bindingHashValidator.Validate(request.BindingSnapshot.Hashes);
        var hashErrors = hashIssues.Where(i => i.Severity == BindingHashValidationSeverity.Error).ToList();
        if (hashErrors.Count > 0)
        {
            var diags = hashErrors.Select(i => new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.BindingHashValidationFailed,
                Severity = SeverityLevel.Error,
                Message = $"Binding hash validation failed at slot '{i.Slot}': {i.Description}"
            }).ToList();
            await RecordAudit(context, null, DescriptorActivationAuditAction.Block, "BindingHashValidationFailed", diags, ct);
            return AgentToolResult<ActivationRequest>.InvalidRequest(diags);
        }

        // Log warnings without blocking
        var hashWarnings = hashIssues.Where(i => i.Severity == BindingHashValidationSeverity.Warning).ToList();
        foreach (var w in hashWarnings)
        {
            _logger.LogWarning("Binding hash warning at slot '{Slot}': {Description}", w.Slot, w.Description);
        }

        // Resolve draft
        var draft = await _draftStore.GetAsync(context.TenantId, request.DraftId, ct);
        if (draft is null)
        {
            return AgentToolResult<ActivationRequest>.NotFound($"Draft '{request.DraftId}' not found.");
        }

        // Use pre-evaluated governance decision from the request, or fail-closed default.
        // Per architecture (memory #153): governance evaluation lives outside RequestService.
        // When no GovernanceDecision is provided, ReviewRequired is the safe default.
        var governanceDecision = request.GovernanceDecision
            ?? DescriptorLifecycleDecisionKind.ReviewRequired;

        // Get policy
        var policy = await _policyProvider.GetPolicyAsync(context.TenantId, draft.DescriptorKind, ct);

        // Derive eligibility
        var eligibility = DeriveEligibility(governanceDecision, policy);

        // Blocked → cannot create activatable request
        if (eligibility == DescriptorActivationEligibility.NotActivatable)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.BlockedByGovernance,
                Severity = SeverityLevel.Error,
                Message = $"Activation is blocked by governance decision: {governanceDecision}."
            };
            await RecordAudit(context, null, DescriptorActivationAuditAction.Block, "GovernanceBlocked", [diag], ct);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        // Determine initial status
        var initialStatus = eligibility == DescriptorActivationEligibility.RequiresHumanReview
            ? ActivationRequestStatus.UnderReview
            : ActivationRequestStatus.Submitted;

        // Create activation request
        var actorKind = DescriptorActivationActorKindExtensions.FromAgentToolActorKind(context.ActorKind)
            ?? DescriptorActivationActorKind.System;

        // Fail-closed: binding snapshot must have complete references
        if (string.IsNullOrWhiteSpace(request.BindingSnapshot.PackagePreviewId)
            || string.IsNullOrWhiteSpace(request.BindingSnapshot.EvidencePreviewId))
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.IncompleteBinding,
                Severity = SeverityLevel.Error,
                Message = "Activation request requires complete binding (PackagePreviewId and EvidencePreviewId must be non-empty)."
            };
            await RecordAudit(context, null, DescriptorActivationAuditAction.Block, "IncompleteBinding", [diag], ct);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        var eligibilityDiagnostics = BuildEligibilityDiagnostics(eligibility, governanceDecision, policy);

        var activationRequest = new ActivationRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            TenantId = context.TenantId,
            DraftId = request.DraftId,
            Status = initialStatus,
            SubmittedAt = DateTimeOffset.UtcNow,
            SubmittedBy = context.ActorId,
            CreatedByActorId = context.ActorId,
            CreatedByActorKind = actorKind,
            GovernanceDecision = governanceDecision,
            Eligibility = eligibility,
            Policy = policy,
            BindingSnapshot = request.BindingSnapshot,
            Diagnostics = eligibilityDiagnostics
        };

        _requests[(context.TenantId, activationRequest.RequestId)] =
            new ActivationResourceSnapshot(activationRequest, draft);

        await RecordAudit(context, activationRequest.RequestId, DescriptorActivationAuditAction.Submit,
            "Created", [], ct, activationRequest);

        // Auto-activate if eligible and policy permits
        if (eligibility == DescriptorActivationEligibility.AutoActivatable
            && policy.AutoActivateAllowedWhenPolicyPermits)
        {
            _logger.LogInformation(
                "Auto-activating request {RequestId} for draft {DraftId} (governance: {Governance})",
                activationRequest.RequestId, request.DraftId, governanceDecision);

            var updatedRequest = activationRequest with
            {
                Status = ActivationRequestStatus.Approved
            };
            _requests[(context.TenantId, activationRequest.RequestId)] =
                new ActivationResourceSnapshot(updatedRequest, draft);

            await RecordAudit(context, activationRequest.RequestId, DescriptorActivationAuditAction.Approve,
                "AutoApproved", eligibilityDiagnostics, ct, updatedRequest);

            // Execute gate for auto-activated request
            var gateResult = await ExecuteActivationGateAsync(context, activationRequest.RequestId, ct);

            // Return the gate result (may be success or failure)
            if (gateResult.Status == AgentToolResultStatus.Success && gateResult.Value is not null)
            {
                return AgentToolResult<ActivationRequest>.Success(gateResult.Value);
            }
            return gateResult;
        }

        return AgentToolResult<ActivationRequest>.Success(activationRequest);
    }

    public async Task<AgentToolResult<DescriptorActivationDecision>> EvaluateActivationEligibilityAsync(
        AgentToolInvocationContext context, string draftId,
        DescriptorLifecycleDecisionKind? governanceDecision = null,
        CancellationToken ct = default)
    {
        var draft = await _draftStore.GetAsync(context.TenantId, draftId, ct);
        if (draft is null)
        {
            return AgentToolResult<DescriptorActivationDecision>.NotFound($"Draft '{draftId}' not found.");
        }

        // Governance evaluation lives outside RequestService (memory #153).
        // When no pre-evaluated GovernanceDecision is provided, fail-closed to ReviewRequired.
        var effectiveDecision = governanceDecision
            ?? DescriptorLifecycleDecisionKind.ReviewRequired;
        var policy = await _policyProvider.GetPolicyAsync(context.TenantId, draft.DescriptorKind, ct);
        var eligibility = DeriveEligibility(effectiveDecision, policy);

        var decision = new DescriptorActivationDecision
        {
            Eligibility = eligibility,
            Policy = policy,
            GovernanceDecision = effectiveDecision,
            Diagnostics = BuildEligibilityDiagnostics(eligibility, effectiveDecision, policy)
        };

        return AgentToolResult<DescriptorActivationDecision>.Success(decision);
    }

    public async Task<AgentToolResult<ActivationRequest>> ApproveActivationRequestAsync(
        AgentToolInvocationContext context, string requestId,
        DescriptorActivationReviewDecision reviewDecision, CancellationToken ct = default, string? completionEventId = null)
    {
        var snapshot = GetRequestSnapshot(context.TenantId, requestId);
        if (snapshot is null)
        {
            return AgentToolResult<ActivationRequest>.NotFound($"Activation request '{requestId}' not found.");
        }

        var request = snapshot.Request;

        if (snapshot.AppliedDecision is not null
            && request.Status is (ActivationRequestStatus.Approved or ActivationRequestStatus.Activated))
            return ClassifyAppliedReview(snapshot, reviewDecision, completionEventId);

        // Fail-closed: BindingSnapshot.Hashes must be present for hash-binding validation.
        if (request.BindingSnapshot.Hashes is null)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.BindingHashesRequired,
                Severity = SeverityLevel.Error,
                Message = "BindingSnapshot.Hashes is required for approval."
            };
            await RecordAudit(context, requestId, DescriptorActivationAuditAction.GateDenied, "MissingBindingHashes", [diag], ct, request);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        // Verify the review decision targets this specific request — prevents misrouted or replayed decisions.
        if (reviewDecision.ActivationRequestId != request.RequestId)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.ReviewRequestMismatch,
                Severity = SeverityLevel.Error,
                Message = $"Review decision targets request '{reviewDecision.ActivationRequestId}', but current request is '{request.RequestId}'."
            };
            await RecordAudit(context, requestId, DescriptorActivationAuditAction.Block,
                "ReviewDecisionRequestMismatch", [diag], ct, request);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        // Verify the review decision is actually Approved
        if (reviewDecision.Decision != DescriptorActivationReviewOutcome.Approved)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.ReviewDecisionMismatch,
                Severity = SeverityLevel.Error,
                Message = $"ApproveActivationRequestAsync called with decision '{reviewDecision.Decision}', expected 'Approved'."
            };
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        // Verify the review decision is bound to the same evidence as the request.
        // Inconsistency means the reviewer approved a different package/evidence than what the request was bound to.
        if (reviewDecision.BoundEvidenceHash != request.BindingSnapshot.Hashes.PackageEvidenceHash)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.ReviewEvidenceMismatch,
                Severity = SeverityLevel.Error,
                Message = $"Review decision evidence hash '{reviewDecision.BoundEvidenceHash}' does not match request binding hash '{request.BindingSnapshot.Hashes.PackageEvidenceHash}'."
            };
            await RecordAudit(context, requestId, DescriptorActivationAuditAction.Block,
                "ReviewEvidenceMismatch", [diag], ct, request);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        if (reviewDecision.BoundEnvelopeHash != request.BindingSnapshot.Hashes.PackageEvidenceEnvelopeHash)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.ReviewEnvelopeMismatch,
                Severity = SeverityLevel.Error,
                Message = $"Review decision envelope hash '{reviewDecision.BoundEnvelopeHash}' does not match request binding hash '{request.BindingSnapshot.Hashes.PackageEvidenceEnvelopeHash}'."
            };
            await RecordAudit(context, requestId, DescriptorActivationAuditAction.Block,
                "ReviewEnvelopeMismatch", [diag], ct, request);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        // Validate status
        if (request.Status != ActivationRequestStatus.UnderReview
            && request.Status != ActivationRequestStatus.Submitted)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.InvalidStatusForApproval,
                Severity = SeverityLevel.Error,
                Message = $"Cannot approve request in status '{request.Status}'. Expected 'UnderReview' or 'Submitted'."
            };
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        // Self-approval check (ActorId only — same actor bypassing self-approval by switching ActorKind is a security boundary violation)
        // Use the policy captured at request creation time, not a live lookup, to prevent policy-change attacks.
        var policy = request.Policy ?? await _policyProvider.GetPolicyAsync(context.TenantId, snapshot.Owner?.DescriptorKind, ct);
        if (policy.ForbidSelfApproval
            && request.CreatedByActorId == reviewDecision.ActorId)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.SelfApprovalForbidden,
                Severity = SeverityLevel.Error,
                Message = $"Actor '{reviewDecision.ActorId}' cannot approve their own activation request when ForbidSelfApproval is enabled."
            };
            await RecordAudit(context, requestId, DescriptorActivationAuditAction.Block,
                "SelfApprovalForbidden", [diag], ct, request);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        // Transition to Approved
        var updatedRequest = request with { Status = ActivationRequestStatus.Approved };
        _requests[(context.TenantId, requestId)] = snapshot with
        {
            Request = updatedRequest,
            AppliedCompletionEventId = completionEventId,
            AppliedDecision = reviewDecision
        };

        await RecordAudit(context, requestId, DescriptorActivationAuditAction.Approve,
            "Approved", [], ct, updatedRequest);

        // Evidence recheck + gate execution (matches interface contract: "rechecks evidence hashes, then calls Runtime Activation Gate")
        return await ExecuteActivationGateAsync(context, requestId, ct);
    }

    public async Task<AgentToolResult<ActivationRequest>> RejectActivationRequestAsync(
        AgentToolInvocationContext context, string requestId,
        DescriptorActivationReviewDecision reviewDecision, CancellationToken ct = default, string? completionEventId = null)
    {
        var snapshot = GetRequestSnapshot(context.TenantId, requestId);
        if (snapshot is null)
        {
            return AgentToolResult<ActivationRequest>.NotFound($"Activation request '{requestId}' not found.");
        }

        var request = snapshot.Request;

        if (snapshot.AppliedDecision is not null && request.Status == ActivationRequestStatus.Rejected)
            return ClassifyAppliedReview(snapshot, reviewDecision, completionEventId);

        // Fail-closed: BindingSnapshot.Hashes must be present.
        if (request.BindingSnapshot.Hashes is null)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.BindingHashesRequired,
                Severity = SeverityLevel.Error,
                Message = "BindingSnapshot.Hashes is required for rejection."
            };
            await RecordAudit(context, requestId, DescriptorActivationAuditAction.GateDenied, "MissingBindingHashes", [diag], ct);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        // Verify the review decision targets this specific request — symmetric with approve path.
        if (reviewDecision.ActivationRequestId != request.RequestId)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.ReviewRequestMismatch,
                Severity = SeverityLevel.Error,
                Message = $"Review decision targets request '{reviewDecision.ActivationRequestId}', but current request is '{request.RequestId}'."
            };
            await RecordAudit(context, requestId, DescriptorActivationAuditAction.Block,
                "ReviewDecisionRequestMismatch", [diag], ct, request);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        // Verify the review decision is actually Rejected
        if (reviewDecision.Decision != DescriptorActivationReviewOutcome.Rejected)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.ReviewDecisionMismatch,
                Severity = SeverityLevel.Error,
                Message = $"RejectActivationRequestAsync called with decision '{reviewDecision.Decision}', expected 'Rejected'."
            };
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        if (request.Status != ActivationRequestStatus.UnderReview
            && request.Status != ActivationRequestStatus.Submitted)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.InvalidStatusForRejection,
                Severity = SeverityLevel.Error,
                Message = $"Cannot reject request in status '{request.Status}'. Expected 'UnderReview' or 'Submitted'."
            };
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        var updatedRequest = request with { Status = ActivationRequestStatus.Rejected };
        _requests[(context.TenantId, requestId)] = snapshot with
        {
            Request = updatedRequest,
            AppliedCompletionEventId = completionEventId,
            AppliedDecision = reviewDecision
        };

        await RecordAudit(context, requestId, DescriptorActivationAuditAction.Reject,
            "Rejected", [], ct, updatedRequest);

        return AgentToolResult<ActivationRequest>.Success(updatedRequest);
    }

    public async Task<AgentToolResult<ActivationRequest>> RecheckEvidenceAsync(
        AgentToolInvocationContext context, string requestId, CancellationToken ct = default)
    {
        var snapshot = GetRequestSnapshot(context.TenantId, requestId);
        if (snapshot is null)
        {
            return AgentToolResult<ActivationRequest>.NotFound($"Activation request '{requestId}' not found.");
        }

        var request = snapshot.Request;

        // Terminal states don't need recheck
        if (request.Status == ActivationRequestStatus.Activated
            || request.Status == ActivationRequestStatus.ActivationFailed
            || request.Status == ActivationRequestStatus.Rejected
            || request.Status == ActivationRequestStatus.Cancelled
            || request.Status == ActivationRequestStatus.Expired
            || request.Status == ActivationRequestStatus.Stale)
        {
            return AgentToolResult<ActivationRequest>.Success(request);
        }

        // Run evidence recheck
        var recheckResult = await _evidenceRechecker.RecheckAsync(
            context.TenantId, request.BindingSnapshot, ct);

        if (recheckResult.IsStale)
        {
            var staleDiagnostics = recheckResult.Drifts.Select(d => new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.EvidenceStale,
                Severity = SeverityLevel.Error,
                Message = $"Evidence drift detected: {d.FieldName} changed from '{d.BoundHashValue}' to '{d.CurrentHashValue}'."
            }).ToList();

            var updatedRequest = request with { Status = ActivationRequestStatus.Stale };
            _requests[(context.TenantId, requestId)] = snapshot with { Request = updatedRequest };

            await RecordAudit(context, requestId, DescriptorActivationAuditAction.Stale,
                "EvidenceStale", staleDiagnostics, ct, updatedRequest);

            return AgentToolResult<ActivationRequest>.Success(updatedRequest);
        }

        return AgentToolResult<ActivationRequest>.Success(request);
    }

    public async Task<AgentToolResult<ActivationRequest>> ExecuteActivationGateAsync(
        AgentToolInvocationContext context, string requestId, CancellationToken ct = default)
    {
        var snapshot = GetRequestSnapshot(context.TenantId, requestId);
        if (snapshot is null)
        {
            return AgentToolResult<ActivationRequest>.NotFound($"Activation request '{requestId}' not found.");
        }

        var request = snapshot.Request;

        // Gate cannot be called for non-activatable states
        if (request.Status != ActivationRequestStatus.Approved
            && request.Status != ActivationRequestStatus.Submitted)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.GateInvalidState,
                Severity = SeverityLevel.Error,
                Message = $"Cannot execute activation gate for request in status '{request.Status}'. Expected 'Approved' or 'Submitted'."
            };
            await RecordAudit(context, requestId, DescriptorActivationAuditAction.GateDenied,
                "GateInvalidState", [diag], ct, request);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        if (request.Eligibility == DescriptorActivationEligibility.NotActivatable)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.GateBlocked,
                Severity = SeverityLevel.Error,
                Message = "Cannot execute activation gate for NotActivatable request."
            };
            await RecordAudit(context, requestId, DescriptorActivationAuditAction.GateDenied,
                "GateBlocked", [diag], ct, request);
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        // Evidence recheck before gate execution
        var recheckResult = await _evidenceRechecker.RecheckAsync(
            context.TenantId, request.BindingSnapshot, ct);

        if (recheckResult.IsStale)
        {
            var staleDiagnostics = recheckResult.Drifts.Select(d => new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.EvidenceStale,
                Severity = SeverityLevel.Error,
                Message = $"Evidence drift detected: {d.FieldName} changed from '{d.BoundHashValue}' to '{d.CurrentHashValue}'."
            }).ToList();

            var staleRequest = request with { Status = ActivationRequestStatus.Stale };
            _requests[(context.TenantId, requestId)] = snapshot with { Request = staleRequest };

            await RecordAudit(context, requestId, DescriptorActivationAuditAction.Stale,
                "EvidenceStale", staleDiagnostics, ct, staleRequest);

            return AgentToolResult<ActivationRequest>.InvalidRequest(staleDiagnostics);
        }

        // Execute the gate — ONLY component that mutates active descriptor/runtime state
        var gateResult = await _activationGate.ActivateAsync(context, request, ct);

        if (gateResult.Status != AgentToolResultStatus.Success)
        {
            var failedRequest = request with { Status = ActivationRequestStatus.ActivationFailed };
            _requests[(context.TenantId, requestId)] = snapshot with { Request = failedRequest };

            await RecordAudit(context, requestId, DescriptorActivationAuditAction.GateDenied,
                "GateRejected", gateResult.Diagnostics, ct, failedRequest);
            return AgentToolResult<ActivationRequest>.Failed(gateResult.Diagnostics);
        }

        // Transition to Activated — gate executed successfully
        var activatedRequest = request with { Status = ActivationRequestStatus.Activated };
        _requests[(context.TenantId, requestId)] = snapshot with { Request = activatedRequest };

        await RecordAudit(context, requestId, DescriptorActivationAuditAction.Activate,
            "GateExecuted", [], ct, activatedRequest);

        _logger.LogInformation(
            "Activation gate executed successfully for request {RequestId}, draft {DraftId}",
            requestId, request.DraftId);

        return AgentToolResult<ActivationRequest>.Success(activatedRequest);
    }

    public async Task<AgentToolResult<ActivationRequest>> CancelActivationRequestAsync(
        AgentToolInvocationContext context, string requestId, string reason, CancellationToken ct = default)
    {
        var snapshot = GetRequestSnapshot(context.TenantId, requestId);
        if (snapshot is null)
        {
            return AgentToolResult<ActivationRequest>.NotFound($"Activation request '{requestId}' not found.");
        }

        var request = snapshot.Request;

        if (request.Status != ActivationRequestStatus.Submitted
            && request.Status != ActivationRequestStatus.UnderReview)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.CannotCancel,
                Severity = SeverityLevel.Error,
                Message = $"Cannot cancel request in status '{request.Status}'. Only 'Submitted' or 'UnderReview' can be cancelled."
            };
            return AgentToolResult<ActivationRequest>.InvalidRequest([diag]);
        }

        var updatedRequest = request with { Status = ActivationRequestStatus.Cancelled };
        _requests[(context.TenantId, requestId)] = snapshot with { Request = updatedRequest };

        await RecordAudit(context, requestId, DescriptorActivationAuditAction.Cancel,
            reason, [], ct, updatedRequest);

        return AgentToolResult<ActivationRequest>.Success(updatedRequest);
    }

    public Task<AgentToolResult<ActivationRequest>> GetActivationRequestStatusAsync(
        AgentToolInvocationContext context, string requestId, CancellationToken ct = default)
    {
        var snapshot = GetRequestSnapshot(context.TenantId, requestId);
        if (snapshot is null)
        {
            return Task.FromResult(
                AgentToolResult<ActivationRequest>.NotFound($"Activation request '{requestId}' not found."));
        }

        return Task.FromResult(AgentToolResult<ActivationRequest>.Success(snapshot.Request));
    }

    // ── Internal helpers ──

    private ActivationResourceSnapshot? GetRequestSnapshot(string tenantId, string requestId)
        => _requests.TryGetValue((tenantId, requestId), out var snapshot) ? snapshot : null;

    private static AgentToolResult<ActivationRequest> ClassifyAppliedReview(
        ActivationResourceSnapshot snapshot,
        DescriptorActivationReviewDecision decision,
        string? completionEventId)
    {
        var same = !string.IsNullOrWhiteSpace(completionEventId)
            && string.Equals(snapshot.AppliedCompletionEventId, completionEventId, StringComparison.Ordinal)
            && DecisionsEqual(snapshot.AppliedDecision!, decision);
        if (same)
        {
            return AgentToolResult<ActivationRequest>.SucceededWithDiagnostics(
                snapshot.Request,
                [new AgentToolDiagnostic
                {
                    Code = DescriptorActivationDiagnosticCodes.ReviewDuplicate,
                    Severity = SeverityLevel.Info,
                    Message = "The exact activation review completion was already applied."
                }]);
        }

        return AgentToolResult<ActivationRequest>.InvalidRequest(
            [new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.ReviewConflict,
                Severity = SeverityLevel.Error,
                Message = "The activation request already has a different durable review decision."
            }]);
    }

    private static bool DecisionsEqual(
        DescriptorActivationReviewDecision left,
        DescriptorActivationReviewDecision right)
        => string.Equals(left.ActivationRequestId, right.ActivationRequestId, StringComparison.Ordinal)
            && string.Equals(left.TenantId, right.TenantId, StringComparison.Ordinal)
            && string.Equals(left.CorrelationId, right.CorrelationId, StringComparison.Ordinal)
            && left.Decision == right.Decision
            && left.ActorKind == right.ActorKind
            && string.Equals(left.ActorId, right.ActorId, StringComparison.Ordinal)
            && string.Equals(left.Reason, right.Reason, StringComparison.Ordinal)
            && left.DecidedAt == right.DecidedAt
            && string.Equals(left.BoundEvidenceHash.Value, right.BoundEvidenceHash.Value, StringComparison.Ordinal)
            && string.Equals(left.BoundEnvelopeHash.Value, right.BoundEnvelopeHash.Value, StringComparison.Ordinal);

    protected static DescriptorActivationEligibility DeriveEligibility(
        DescriptorLifecycleDecisionKind governanceDecision,
        DescriptorActivationPolicy policy)
    {
        if (governanceDecision == DescriptorLifecycleDecisionKind.Blocked)
            return DescriptorActivationEligibility.NotActivatable;

        if (policy.RequireHumanReviewForAll)
            return DescriptorActivationEligibility.RequiresHumanReview;

        if (governanceDecision == DescriptorLifecycleDecisionKind.ReviewRequired)
            return DescriptorActivationEligibility.RequiresHumanReview;

        return DescriptorActivationEligibility.AutoActivatable;
    }

    private static IReadOnlyList<AgentToolDiagnostic> BuildEligibilityDiagnostics(
        DescriptorActivationEligibility eligibility,
        DescriptorLifecycleDecisionKind governanceDecision,
        DescriptorActivationPolicy policy)
    {
        var diagnostics = new List<AgentToolDiagnostic>();

        if (eligibility == DescriptorActivationEligibility.NotActivatable)
        {
            diagnostics.Add(new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.GovernanceBlocked,
                Severity = SeverityLevel.Error,
                Message = $"Activation blocked by governance decision: {governanceDecision}."
            });
        }
        else if (eligibility == DescriptorActivationEligibility.RequiresHumanReview)
        {
            diagnostics.Add(new AgentToolDiagnostic
            {
                Code = DescriptorActivationDiagnosticCodes.RequiresHumanReview,
                Severity = SeverityLevel.Warning,
                Message = policy.RequireHumanReviewForAll
                    ? "Activation requires human review (policy: RequireHumanReviewForAll)."
                    : $"Activation requires human review (governance: {governanceDecision})."
            });
        }

        return diagnostics.AsReadOnly();
    }

    private async Task RecordAudit(
        AgentToolInvocationContext context,
        string? requestId,
        DescriptorActivationAuditAction action,
        string outcome,
        IReadOnlyList<AgentToolDiagnostic> diagnostics,
        CancellationToken ct,
        ActivationRequest? request = null)
    {
        var record = new DescriptorActivationAuditRecord
        {
            AuditRecordId = Guid.NewGuid().ToString("N"),
            ActivationRequestId = requestId ?? string.Empty,
            TenantId = context.TenantId,
            Action = action,
            ActorKind = DescriptorActivationActorKindExtensions.FromAgentToolActorKind(context.ActorKind)
                ?? DescriptorActivationActorKind.System,
            ActorId = context.ActorId,
            TargetDescriptorRef = request?.DraftId,
            Outcome = outcome,
            CorrelationId = context.CorrelationId,
            EvidenceHash = request?.BindingSnapshot?.Hashes?.PackageEvidenceHash,
            EnvelopeHash = request?.BindingSnapshot?.Hashes?.PackageEvidenceEnvelopeHash,
            Timestamp = DateTimeOffset.UtcNow
        };

        await _auditor.RecordAsync(record, ct);
    }
}
