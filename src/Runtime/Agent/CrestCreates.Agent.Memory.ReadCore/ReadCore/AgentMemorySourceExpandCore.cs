using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.ReadCore;

/// <summary>
/// Shared source expand core. Protocol-neutral. Zero artifact writes —
/// ctx_expand and memory_source_expand must not issue any handles/grants.
/// </summary>
internal sealed class AgentMemorySourceExpandCore : IAgentMemorySourceExpandCore
{
    private readonly IAgentMemoryAccessGrantResolver _grantResolver;
    private readonly IAgentContextSourceExpander _expander;

    public AgentMemorySourceExpandCore(
        IAgentMemoryAccessGrantResolver grantResolver,
        IAgentContextSourceExpander expander)
    {
        _grantResolver = grantResolver;
        _expander = expander;
    }

    public async ValueTask<AgentMemoryReadCoreOutcome<ExpandAgentMemorySourceResult>> ExpandAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        ExpandAgentMemorySourceInput input,
        CancellationToken cancellationToken = default)
    {
        // Validate budget
        if (input.MaximumCharacters <= 0)
            throw new AgentMemoryReadCoreException("budget-invalid", "MaximumCharacters must be positive");
        if (input.MaximumCharacters > scope.MaxExpansionCharacters)
            throw new AgentMemoryReadCoreException("budget-invalid", "MaximumCharacters exceeds scope limit");

        // Resolve grant — validates the caller holds a grant to expand this source
        var grant = await _grantResolver.ResolveAsync(
            input.GrantId, principal, scope, cancellationToken);
        if (grant is null)
            throw new AgentMemoryReadCoreException("resource-unavailable", "Grant not resolvable");

        // Expand — zero artifact writes
        var expansion = await _expander.ExpandAsync(grant.SourceRef, cancellationToken);

        // Map result based on expansion status
        var result = expansion.Status switch
        {
            AgentMemorySourceExpansionStatus.Expanded => new ExpandAgentMemorySourceResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.Completed,
                SanitizedContent = expansion.SanitizedContent is { Length: > 0 } content
                    ? (content.Length > input.MaximumCharacters
                        ? content[..input.MaximumCharacters]
                        : content)
                    : null,
                WasTruncated = (expansion.SanitizedContent?.Length ?? 0) > input.MaximumCharacters,
                Diagnostics = expansion.Diagnostics.Select(d => new AgentMemoryToolDiagnosticDto
                {
                    Code = d.Code.RequireValue(),
                    Severity = MapSeverity(d.Severity)
                }).ToList()
            },
            AgentMemorySourceExpansionStatus.Redacted => new ExpandAgentMemorySourceResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.Redacted
            },
            _ => new ExpandAgentMemorySourceResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.NotExpandable
            }
        };

        // Zero artifact writes — no handles, no grants, no compensation token
        return new AgentMemoryReadCoreOutcome<ExpandAgentMemorySourceResult>
        {
            Result = result,
            ScopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope),
            MaximumAuditFacts = scope.MaxAuditFacts,
            Receipt = new AgentMemoryArtifactBatchReceipt
            {
                HandleBatch = null,
                GrantBatch = null
            },
            CompensationToken = null
        };
    }

    private static AgentMemoryToolDiagnosticSeverity MapSeverity(SeverityLevel severity)
    {
        var value = severity.RequireValue();
        if (string.Equals(value, "Error", StringComparison.Ordinal) || string.Equals(value, "Blocker", StringComparison.Ordinal))
            return AgentMemoryToolDiagnosticSeverity.Error;
        if (string.Equals(value, "Warning", StringComparison.Ordinal) || string.Equals(value, "Review", StringComparison.Ordinal))
            return AgentMemoryToolDiagnosticSeverity.Warning;
        if (string.Equals(value, "Info", StringComparison.Ordinal))
            return AgentMemoryToolDiagnosticSeverity.Info;
        return AgentMemoryToolDiagnosticSeverity.Unknown;
    }
}
