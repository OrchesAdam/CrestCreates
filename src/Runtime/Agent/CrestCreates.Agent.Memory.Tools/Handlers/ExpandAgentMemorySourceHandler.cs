using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

[CapabilityName(AgentMemoryToolCapabilityIds.ExpandSource)]
internal sealed class ExpandAgentMemorySourceHandler : AgentMemoryToolHandlerBase, ICapabilityHandler<ExpandAgentMemorySourceInput, ExpandAgentMemorySourceResult>
{
    private readonly IAgentMemoryToolAccessScopeProvider _scopeProvider;
    private readonly IAgentMemorySourceGrantResolver _grantResolver;
    private readonly IAgentContextSourceExpander _expander;

    public ExpandAgentMemorySourceHandler(
        ICapabilityExecutionContextAccessor capabilityContext,
        IAgentExecutionContextAccessor agentExecution,
        IAgentMemoryToolAccessScopeProvider scopeProvider,
        IAgentMemorySourceGrantResolver grantResolver,
        IAgentContextSourceExpander expander)
        : base(capabilityContext, agentExecution)
    {
        _scopeProvider = scopeProvider;
        _grantResolver = grantResolver;
        _expander = expander;
    }

    public async Task<ExpandAgentMemorySourceResult> ExecuteAsync(ExpandAgentMemorySourceInput input, CancellationToken ct)
    {
        var principal = Principal;
        var scope = await _scopeProvider.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (!IsValidScope(scope)) return Prepare(scope, Unavailable("scope-invalid"));
        if (input.MaximumCharacters <= 0 || input.MaximumCharacters > scope.MaxExpansionCharacters)
            return Prepare(scope, Unavailable("budget-invalid"));
        var grant = await _grantResolver.ResolveAsync(input.GrantId, principal, scope, ct).ConfigureAwait(false);
        if (grant is null)
            return Prepare(scope, Unavailable("source-unavailable"));

        var expanded = await _expander.ExpandAsync(grant.SourceRef, ct).ConfigureAwait(false);
        var result = expanded.Status switch
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
        return Prepare(scope, result);
    }

    private static ExpandAgentMemorySourceResult Unavailable(string code) => new()
    {
        OperationStatus = AgentMemoryToolOperationStatus.Unavailable,
        SanitizedContent = null,
        CanonicalContentHash = null,
        WasTruncated = false,
        Diagnostics = [Diagnostic(code)]
    };

    private ExpandAgentMemorySourceResult Prepare(AgentMemoryToolAccessScope scope, ExpandAgentMemorySourceResult result)
    {
        AddBranchInvariantFacts(scope, "expand-memory-source");
        PublishAllowedOutcomes((WireStatus(result.OperationStatus),
            PrepareOutput(result, AgentMemoryToolJsonSerializerContext.Default.ExpandAgentMemorySourceResult)));
        return result;
    }

    private static string WireStatus(AgentMemoryToolOperationStatus status) => status switch
    {
        AgentMemoryToolOperationStatus.Completed => "completed",
        AgentMemoryToolOperationStatus.Unavailable => "unavailable",
        AgentMemoryToolOperationStatus.Conflict => "conflict",
        AgentMemoryToolOperationStatus.Redacted => "redacted",
        AgentMemoryToolOperationStatus.NotExpandable => "not-expandable",
        _ => throw new InvalidOperationException("Unknown Memory Tool operation status.")
    };
}
