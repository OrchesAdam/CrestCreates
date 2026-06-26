using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// In-memory runtime activation gate for Phase 7e.
/// Records activation events without actual runtime state mutation.
/// Production implementation (with real descriptor activation) deferred to runtime module.
/// </summary>
public sealed class InMemoryRuntimeActivationGate : IRuntimeActivationGate
{
    private readonly ILogger<InMemoryRuntimeActivationGate> _logger;
    private readonly ActivationBindingHashValidator _bindingHashValidator;

    public InMemoryRuntimeActivationGate(
        ILogger<InMemoryRuntimeActivationGate> logger,
        ActivationBindingHashValidator bindingHashValidator)
    {
        _logger = logger;
        _bindingHashValidator = bindingHashValidator;
    }

    /// <summary>
    /// When true, the gate rejects all activation requests.
    /// For testing rejection paths only.
    /// </summary>
    public bool CanReject { get; set; }

    public Task<AgentToolResult<RuntimeActivationGateResult>> ActivateAsync(
        AgentToolInvocationContext context, ActivationRequest request, CancellationToken ct = default)
    {
        // Validate binding hashes before gate execution — malformed hashes block activation
        if (request.BindingSnapshot?.Hashes is not null)
        {
            var hashIssues = _bindingHashValidator.Validate(request.BindingSnapshot.Hashes);
            var hashErrors = hashIssues.Where(i => i.Severity == BindingHashValidationSeverity.Error).ToList();
            if (hashErrors.Count > 0)
            {
                _logger.LogWarning(
                    "Runtime activation gate: BLOCKED for draft {DraftId}, request {RequestId} — binding hash validation failed",
                    request.DraftId, request.RequestId);

                var diags = hashErrors.Select(i => new AgentToolDiagnostic
                {
                    Code = DescriptorActivationDiagnosticCodes.BindingHashValidationFailed,
                    Severity = AgentToolDiagnosticSeverity.Error,
                    Message = $"Binding hash validation failed at slot '{i.Slot}': {i.Description}"
                }).ToList();

                return Task.FromResult(
                    AgentToolResult<RuntimeActivationGateResult>.Failed(diags));
            }
        }

        if (CanReject)
        {
            _logger.LogInformation(
                "In-memory activation gate: REJECTING activation for draft {DraftId}, request {RequestId} (CanReject=true)",
                request.DraftId, request.RequestId);

            return Task.FromResult(
                AgentToolResult<RuntimeActivationGateResult>.Failed(
                    [new AgentToolDiagnostic
                    {
                        Code = AgentToolDiagnosticCodes.RuntimeActivationGateRejected,
                        Severity = AgentToolDiagnosticSeverity.Error,
                        Message = "In-memory gate rejection (CanReject=true)."
                    }]));
        }

        _logger.LogInformation(
            "In-memory activation gate: activating draft {DraftId} for tenant {TenantId}, request {RequestId}",
            request.DraftId, request.TenantId, request.RequestId);

        var result = new RuntimeActivationGateResult
        {
            ActivatedDescriptorRef = $"activated:{request.DraftId}",
            DraftId = request.DraftId,
            TenantId = request.TenantId,
            ActivatedAt = DateTimeOffset.UtcNow
        };

        return Task.FromResult(AgentToolResult<RuntimeActivationGateResult>.Success(result));
    }
}
