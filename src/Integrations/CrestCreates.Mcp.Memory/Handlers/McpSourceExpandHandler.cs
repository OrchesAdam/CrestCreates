using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.ReadCore;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Mcp.Memory.Security;

namespace CrestCreates.Mcp.Memory.Handlers;

[CapabilityName("mcp-memory:agent.source.expand")]
internal sealed class McpSourceExpandHandler : ICapabilityHandler<ExpandAgentMemorySourceInput, ExpandAgentMemorySourceResult>
{
    private readonly IAgentMemorySourceExpandCore _readCore;
    private readonly IAgentMemoryAccessScopeProvider _scopeProvider;
    private readonly McpMemoryArtifactOriginFactory _originFactory;
    private readonly ICapabilityExecutionContextAccessor _contextAccessor;

    public McpSourceExpandHandler(
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
