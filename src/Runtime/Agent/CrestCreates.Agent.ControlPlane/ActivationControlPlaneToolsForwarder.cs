using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;

namespace CrestCreates.Agent.ControlPlane;

internal sealed class ActivationControlPlaneToolsForwarder : IActivationControlPlaneTools
{
    private readonly IAgentControlPlaneToolService _facade;

    public ActivationControlPlaneToolsForwarder(IAgentControlPlaneToolService facade)
    {
        _facade = facade;
    }

    public Task<AgentToolResult<ActivationReadinessPreview>> BuildActivationReadinessPreviewAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default) => _facade.BuildActivationReadinessPreviewAsync(context, draftId, ct);

    public Task<AgentToolResult<ActivationRequest>> SubmitActivationRequestAsync(
        AgentToolInvocationContext context,
        SubmitActivationRequestRequest request,
        CancellationToken ct = default) => _facade.SubmitActivationRequestAsync(context, request, ct);

    public Task<AgentToolResult<ActivationRequest>> GetActivationRequestStatusAsync(
        AgentToolInvocationContext context,
        string requestId,
        CancellationToken ct = default) => _facade.GetActivationRequestStatusAsync(context, requestId, ct);

    public Task<AgentToolResult<ActivationRequest>> CancelActivationRequestAsync(
        AgentToolInvocationContext context,
        string requestId,
        CancellationToken ct = default) => _facade.CancelActivationRequestAsync(context, requestId, ct);
}
