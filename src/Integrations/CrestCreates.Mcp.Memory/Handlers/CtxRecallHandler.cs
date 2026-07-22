using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.ReadCore;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Mcp.Memory.Security;

namespace CrestCreates.Mcp.Memory.Handlers;

/// <summary>
/// ctx_recall: resolves a context handle and returns the recalled content.
/// May issue new resource handles (context handle → resource handle).
/// </summary>
[CapabilityName("mcp.ctx_recall")]
internal sealed class CtxRecallHandler : ICapabilityHandler<RecallAgentContextInput, RecallAgentContextResult>
{
    private readonly IAgentContextReadCore _readCore;
    private readonly IAgentMemoryAccessScopeProvider _scopeProvider;
    private readonly McpMemoryArtifactOriginFactory _originFactory;
    private readonly ICapabilityExecutionContextAccessor _contextAccessor;

    public CtxRecallHandler(
        IAgentContextReadCore readCore,
        IAgentMemoryAccessScopeProvider scopeProvider,
        McpMemoryArtifactOriginFactory originFactory,
        ICapabilityExecutionContextAccessor contextAccessor)
    {
        _readCore = readCore;
        _scopeProvider = scopeProvider;
        _originFactory = originFactory;
        _contextAccessor = contextAccessor;
    }

    public async Task<RecallAgentContextResult> ExecuteAsync(
        RecallAgentContextInput input,
        CancellationToken ct)
    {
        var context = _contextAccessor.Current
            ?? throw new InvalidOperationException("CapabilityExecutionContext is not available.");

        var principal = _originFactory.CreatePrincipal(context);
        var origin = _originFactory.CreateInvocationOrigin(context);
        var scope = await _scopeProvider.ResolveAsync(principal, ct);

        var outcome = await _readCore.RecallContextAsync(principal, origin, scope, input, ct);
        return outcome.Result;
    }
}
