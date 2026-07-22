using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.ReadCore;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Mcp.Memory.Security;

namespace CrestCreates.Mcp.Memory.Handlers;

/// <summary>
/// memory_source_expand: resolves a source grant and expands the raw source content.
/// Zero artifact writes — no handles, no grants, no compensation token.
/// </summary>
[CapabilityName("mcp.memory_source_expand")]
internal sealed class MemorySourceExpandHandler : ICapabilityHandler<ExpandAgentMemorySourceInput, ExpandAgentMemorySourceResult>
{
    private readonly IAgentMemorySourceExpandCore _readCore;
    private readonly IAgentMemoryAccessScopeProvider _scopeProvider;
    private readonly McpMemoryArtifactOriginFactory _originFactory;
    private readonly ICapabilityExecutionContextAccessor _contextAccessor;

    public MemorySourceExpandHandler(
        IAgentMemorySourceExpandCore readCore,
        IAgentMemoryAccessScopeProvider scopeProvider,
        McpMemoryArtifactOriginFactory originFactory,
        ICapabilityExecutionContextAccessor contextAccessor)
    {
        _readCore = readCore;
        _scopeProvider = scopeProvider;
        _originFactory = originFactory;
        _contextAccessor = contextAccessor;
    }

    public async Task<ExpandAgentMemorySourceResult> ExecuteAsync(
        ExpandAgentMemorySourceInput input,
        CancellationToken ct)
    {
        var context = _contextAccessor.Current
            ?? throw new InvalidOperationException("CapabilityExecutionContext is not available.");

        var principal = _originFactory.CreatePrincipal(context);
        var origin = _originFactory.CreateInvocationOrigin(context);
        var scope = await _scopeProvider.ResolveAsync(principal, ct);

        var outcome = await _readCore.ExpandAsync(principal, origin, scope, input, ct);
        return outcome.Result;
    }
}
