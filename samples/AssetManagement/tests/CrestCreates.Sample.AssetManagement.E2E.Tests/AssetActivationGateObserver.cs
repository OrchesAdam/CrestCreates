using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// Test-side observer over the activation gate — the only component authorized to mutate
/// runtime state. "No deployment" is measured here instead of inferred from the absence of
/// an approved request.
/// </summary>
internal sealed class AssetActivationGateObserver : IRuntimeActivationGate
{
    private readonly InMemoryRuntimeActivationGate _inner;

    public AssetActivationGateObserver(
        ActivationBindingHashValidator bindingHashValidator,
        ILogger<InMemoryRuntimeActivationGate> logger)
        => _inner = new InMemoryRuntimeActivationGate(logger, bindingHashValidator);

    public int CallCount { get; private set; }

    public int SuccessCount { get; private set; }

    public async Task<AgentToolResult<RuntimeActivationGateResult>> ActivateAsync(
        AgentToolInvocationContext context,
        ActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        var result = await _inner.ActivateAsync(context, request, cancellationToken);
        if (result.Status == AgentToolResultStatus.Success)
            SuccessCount++;
        return result;
    }
}
