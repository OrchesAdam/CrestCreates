using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.ReadCore;
using CrestCreates.Agent.Memory.ReadCore.Accountability;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Mcp.Memory.Security;
using CrestCreates.Mcp.Abstractions;

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
    private readonly IAgentMemoryOperationIdentityFactory _identities;
    private readonly IAuditOperationContextAccessor _auditContexts;

    public MemoryRecallHandler(
        IAgentMemoryReadCore readCore,
        IAgentMemoryAccessScopeProvider scopeProvider,
        McpMemoryArtifactOriginFactory originFactory,
        ICapabilityExecutionContextAccessor contextAccessor,
        IAgentMemoryOperationIdentityFactory identities,
        IAuditOperationContextAccessor auditContexts)
    {
        _readCore = readCore;
        _scopeProvider = scopeProvider;
        _originFactory = originFactory;
        _contextAccessor = contextAccessor;
        _identities = identities;
        _auditContexts = auditContexts;
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
        var identity = _identities.Create();
        var causality = AgentMemoryCapabilityCausalityMapper.FromCapability(context, _auditContexts.Current);

        var request = new AgentMemoryRecallOperationRequest
        {
            Principal = principal,
            Origin = origin,
            Identity = identity,
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = principal.TenantId,
                ActorId = context.AccountabilityActor!.Id,
                ActorKind = context.AccountabilityActor.Kind,
                CorrelationId = causality.CorrelationId,
                CausationId = causality.CausationId,
                ParentAuditId = causality.ParentAuditId,
                InvocationId = GetContextItem(context, McpCapabilityContextItemNames.InvocationId),
                SessionId = GetContextItem(context, McpCapabilityContextItemNames.SessionId),
                InvocationSource = AuditInvocationSources.Mcp,
                TraceAttributes = new Dictionary<string, string>
                {
                    ["mcp-capability"] = context.CapabilityId
                }
            },
            Scope = scope,
            Input = input
        };

        var outcome = await _readCore.RecallAsync(request, ct);
        return outcome.Result;
    }

    private static string? GetContextItem(CapabilityExecutionContext context, string key)
        => context.Items.TryGetValue(key, out var value) ? value as string : null;
}
