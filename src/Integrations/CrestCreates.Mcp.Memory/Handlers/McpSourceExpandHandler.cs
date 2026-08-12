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

namespace CrestCreates.Mcp.Memory.Handlers;

[CapabilityName("mcp-memory:agent.source.expand")]
internal sealed class McpSourceExpandHandler : ICapabilityHandler<ExpandAgentMemorySourceInput, ExpandAgentMemorySourceResult>
{
    private readonly IAgentMemorySourceExpandCore _readCore;
    private readonly IAgentMemoryAccessScopeProvider _scopeProvider;
    private readonly McpMemoryArtifactOriginFactory _originFactory;
    private readonly ICapabilityExecutionContextAccessor _contextAccessor;
    private readonly IAgentMemoryOperationIdentityFactory _identities;
    private readonly IAuditOperationContextAccessor _auditContexts;

    public McpSourceExpandHandler(
        IAgentMemorySourceExpandCore readCore,
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

    public async Task<ExpandAgentMemorySourceResult> ExecuteAsync(
        ExpandAgentMemorySourceInput input,
        CancellationToken ct)
    {
        var context = _contextAccessor.Current
            ?? throw new InvalidOperationException("CapabilityExecutionContext is not available.");

        var principal = _originFactory.CreatePrincipal(context);
        var origin = _originFactory.CreateInvocationOrigin(context);
        var scope = await _scopeProvider.ResolveAsync(principal, ct);
        var identity = _identities.Create();
        var causality = AgentMemoryCapabilityCausalityMapper.FromCapability(context, _auditContexts.Current);

        var request = new AgentMemorySourceExpansionOperationRequest
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
                InvocationSource = AuditInvocationSources.Mcp,
                TraceAttributes = new Dictionary<string, string>
                {
                    ["mcp-capability"] = context.CapabilityId
                }
            },
            Scope = scope,
            Input = input
        };

        var outcome = await _readCore.ExpandAsync(request, ct);
        return outcome.Result;
    }
}
