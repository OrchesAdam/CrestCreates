using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

[CapabilityName(AgentMemoryToolCapabilityIds.ExpandSource)]
internal sealed class ExpandAgentMemorySourceHandler : AgentMemoryToolHandlerBase, ICapabilityHandler<ExpandAgentMemorySourceInput, ExpandAgentMemorySourceResult>
{
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemorySourceGrantStore _grants;
    private readonly IAgentContextSourceExpander _expander;

    public ExpandAgentMemorySourceHandler(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemorySourceGrantStore grants,
        IAgentContextSourceExpander expander)
        : base(capabilityContext, agentExecution)
    {
        _scopeProvider = scopeProvider;
        _grants = grants;
        _expander = expander;
    }

    public async Task<ExpandAgentMemorySourceResult> ExecuteAsync(ExpandAgentMemorySourceInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope)) return Unavailable("scope-invalid");
        if (input.MaximumCharacters <= 0 || input.MaximumCharacters > scope.MaxExpansionCharacters)
            return Unavailable("budget-invalid");
        var grant = await _grants.GetAsync(input.GrantId, ct).ConfigureAwait(false);
        if (grant is null || grant.Principal != principal || grant.State != AgentMemorySecurityArtifactState.Active
            || grant.ExpiresAt <= DateTimeOffset.UtcNow
            || !string.Equals(grant.SourceRef.TenantId, principal.TenantId, StringComparison.Ordinal)
            || grant.SourceRef.DescriptorRefs.Any(item => item.Version is not > 0)
            || !IsScopeStillAuthorized(grant, scope))
            return Unavailable("source-unavailable");

        var expanded = await _expander.ExpandAsync(grant.SourceRef, ct).ConfigureAwait(false);
        return expanded.Status switch
        {
            AgentMemorySourceExpansionStatus.Expanded when expanded.SanitizedContent is not null
                => new ExpandAgentMemorySourceResult
                {
                    OperationStatus = AgentMemoryToolOperationStatus.Completed,
                    SanitizedContent = expanded.SanitizedContent.Length <= input.MaximumCharacters
                        ? expanded.SanitizedContent : expanded.SanitizedContent[..input.MaximumCharacters],
                    CanonicalContentHash = expanded.SanitizedContent.Length <= input.MaximumCharacters
                        ? expanded.SourceRef.CanonicalContentHash is not null
                            ? AgentMemoryToolProjection.ToToolHash(expanded.SourceRef.CanonicalContentHash)
                            : null
                        : null,
                    WasTruncated = expanded.SanitizedContent.Length > input.MaximumCharacters,
                    Diagnostics = Array.Empty<AgentMemoryToolDiagnosticDto>()
                },
            AgentMemorySourceExpansionStatus.Redacted => new ExpandAgentMemorySourceResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.Redacted,
                Diagnostics = [Diagnostic("source-redacted")]
            },
            AgentMemorySourceExpansionStatus.NotExpandable or AgentMemorySourceExpansionStatus.ExternalSourceNotSupported => new ExpandAgentMemorySourceResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.NotExpandable,
                Diagnostics = [Diagnostic("source-not-expandable")]
            },
            _ => Unavailable("source-unavailable")
        };
    }

    private static bool IsScopeStillAuthorized(AgentMemorySourceGrant grant, AgentMemoryToolAccessScope scope)
    {
        if (grant.IsUnscoped)
            return scope.AllowUnscopedMemory;
        if (grant.RequiredDescriptorRefs.Any(item => item.Version is not > 0))
            return false;
        var visible = scope.VisibleDescriptorRefs.ToHashSet();
        return grant.RequiredDescriptorRefs.All(visible.Contains);
    }

    private static ExpandAgentMemorySourceResult Unavailable(string code) => new()
    {
        OperationStatus = AgentMemoryToolOperationStatus.Unavailable,
        SanitizedContent = null,
        CanonicalContentHash = null,
        WasTruncated = false,
        Diagnostics = [Diagnostic(code)]
    };
}
