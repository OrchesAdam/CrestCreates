using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.ReadCore;

/// <summary>
/// Shared context recall core. Protocol-neutral.
/// Read-only — resolves existing handle, reads through it, no new artifacts.
/// </summary>
internal sealed class AgentContextReadCore : IAgentContextReadCore
{
    private readonly IAgentMemoryAccessHandleResolver _handleResolver;
    private readonly IAgentCompressedContextStore _contextStore;
    private readonly TimeProvider _timeProvider;

    public AgentContextReadCore(
        IAgentMemoryAccessHandleResolver handleResolver,
        IAgentCompressedContextStore contextStore,
        TimeProvider timeProvider)
    {
        _handleResolver = handleResolver;
        _contextStore = contextStore;
        _timeProvider = timeProvider;
    }

    public async ValueTask<AgentMemoryReadCoreOutcome<RecallAgentContextResult>> RecallContextAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        RecallAgentContextInput input,
        CancellationToken cancellationToken = default)
    {
        // Validate budget
        if (input.MaximumCharacters <= 0)
            throw new AgentMemoryReadCoreException("budget-invalid", "MaximumCharacters must be positive");
        if (input.MaximumCharacters > scope.MaxContextRecallCharacters)
            throw new AgentMemoryReadCoreException("budget-invalid", "MaximumCharacters exceeds scope limit");

        // Resolve context handle
        var resolved = await _handleResolver.ResolveAsync(
            input.ContextHandle, AgentMemoryResourceKind.Context, principal, scope, cancellationToken);
        if (resolved is null)
            throw new AgentMemoryReadCoreException("resource-unavailable", "Context handle not resolvable");

        // Retrieve compressed context
        var context = await _contextStore.GetCompressedContextAsync(
            principal.TenantId, resolved.Handle.ResourceId, cancellationToken);
        if (context is null)
            throw new AgentMemoryReadCoreException("resource-unavailable", "Context not found");

        // Aggregate content from blocks
        var content = context.Blocks is { Count: > 0 }
            ? string.Join("\n", context.Blocks.Select(b => b.Content ?? string.Empty))
            : string.Empty;
        var wasTruncated = content.Length > input.MaximumCharacters;
        if (wasTruncated)
            content = content[..input.MaximumCharacters];

        // Build result — read-only, no new artifacts
        var result = new RecallAgentContextResult
        {
            OperationStatus = AgentMemoryToolOperationStatus.Completed,
            SanitizedContent = content,
            CanonicalContentHash = context.CanonicalOutputHash is not null
                ? new AgentMemoryToolCanonicalHashDto
                {
                    Value = context.CanonicalOutputHash.Value,
                    AlgorithmVersion = context.CanonicalOutputHash.AlgorithmVersion,
                    ContractVersion = context.CanonicalOutputHash.ContractVersion,
                    CanonicalShapeVersion = context.CanonicalOutputHash.CanonicalShapeVersion
                }
                : null,
            WasTruncated = wasTruncated,
            BlockCount = context.Blocks?.Count ?? 0,
            Blocks = context.Blocks?.Select(b => new AgentMemoryToolBlockDto
            {
                Content = b.Content ?? string.Empty,
                CanonicalContentHash = b.CanonicalContentHash is not null
                    ? new AgentMemoryToolCanonicalHashDto
                    {
                        Value = b.CanonicalContentHash.Value,
                        AlgorithmVersion = b.CanonicalContentHash.AlgorithmVersion,
                        ContractVersion = b.CanonicalContentHash.ContractVersion,
                        CanonicalShapeVersion = b.CanonicalContentHash.CanonicalShapeVersion
                    }
                    : new AgentMemoryToolCanonicalHashDto
                    {
                        Value = string.Empty,
                        AlgorithmVersion = "v1",
                        ContractVersion = "v1",
                        CanonicalShapeVersion = "v1"
                    },
                SourceGrants = new List<AgentMemorySourceGrantDto>()
            }).ToList() ?? new List<AgentMemoryToolBlockDto>(),
            Diagnostics = new List<AgentMemoryToolDiagnosticDto>()
        };

        // No new artifacts created — read-only operation
        return new AgentMemoryReadCoreOutcome<RecallAgentContextResult>
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
}
