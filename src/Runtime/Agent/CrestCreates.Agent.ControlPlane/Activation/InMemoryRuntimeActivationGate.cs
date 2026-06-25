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

    public InMemoryRuntimeActivationGate(ILogger<InMemoryRuntimeActivationGate> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// When true, the gate rejects all activation requests.
    /// For testing rejection paths only.
    /// </summary>
    public bool CanReject { get; set; }

    public Task<AgentToolResult<RuntimeActivationGateResult>> ActivateAsync(
        AgentToolInvocationContext context, ActivationRequest request, CancellationToken ct = default)
    {
        if (CanReject)
        {
            _logger.LogInformation(
                "In-memory activation gate: REJECTING activation for draft {DraftId}, request {RequestId} (CanReject=true)",
                request.DraftId, request.RequestId);

            return Task.FromResult(
                AgentToolResult<RuntimeActivationGateResult>.Failed(
                    [new AgentToolDiagnostic
                    {
                        Code = "RUNTIME_ACTIVATION_GATE_REJECTED",
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
