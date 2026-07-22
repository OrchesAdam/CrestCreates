using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.ReadCore;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Mcp.Memory.Security;

namespace CrestCreates.Mcp.Memory.Handlers;

/// <summary>
/// memory_recall: recalls memory items matching the input query.
/// May issue resource handles + source grants.
/// </summary>
[CapabilityName("mcp.memory_recall")]
internal sealed class MemoryRecallHandler : ICapabilityHandler<BuildAgentMemoryPackInput, BuildAgentMemoryPackResult>
{
    private readonly IAgentMemoryReadCore _readCore;
    private readonly IAgentMemoryAccessScopeProvider _scopeProvider;
    private readonly McpMemoryArtifactOriginFactory _originFactory;
    private readonly ICapabilityExecutionContextAccessor _contextAccessor;

    public MemoryRecallHandler(
        IAgentMemoryReadCore readCore,
        IAgentMemoryAccessScopeProvider scopeProvider,
        McpMemoryArtifactOriginFactory originFactory,
        ICapabilityExecutionContextAccessor contextAccessor)
    {
        _readCore = readCore;
        _scopeProvider = scopeProvider;
        _originFactory = originFactory;
        _contextAccessor = contextAccessor;
    }

    public async Task<BuildAgentMemoryPackResult> ExecuteAsync(
        BuildAgentMemoryPackInput input,
        CancellationToken ct)
    {
        var context = _contextAccessor.Current
            ?? throw new InvalidOperationException("CapabilityExecutionContext is not available.");

        var principal = _originFactory.CreatePrincipal(context);
        var origin = _originFactory.CreateInvocationOrigin(context);
        var scope = await _scopeProvider.ResolveAsync(principal, ct);

        var outcome = await _readCore.RecallAsync(principal, origin, scope, input, ct);
        return outcome.Result;
    }
}
