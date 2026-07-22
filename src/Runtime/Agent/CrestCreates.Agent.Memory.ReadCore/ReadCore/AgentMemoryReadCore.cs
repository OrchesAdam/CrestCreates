using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.ReadCore;

/// <summary>
/// Shared memory recall core. Protocol-neutral — used by both Agent Tool and MCP handlers.
/// Does NOT handle audit/outcome preflight — that's the handler's responsibility.
/// </summary>
internal sealed class AgentMemoryReadCore : IAgentMemoryReadCore
{
    private readonly IAgentMemoryRetriever _retriever;
    private readonly IAgentMemoryAccessHandleResolver _handleResolver;
    private readonly IAgentMemoryAccessArtifactCoordinator _coordinator;
    private readonly IAgentMemoryArtifactLifetimePolicy _lifetimePolicy;
    private readonly TimeProvider _timeProvider;

    public AgentMemoryReadCore(
        IAgentMemoryRetriever retriever,
        IAgentMemoryAccessHandleResolver handleResolver,
        IAgentMemoryAccessArtifactCoordinator coordinator,
        IAgentMemoryArtifactLifetimePolicy lifetimePolicy,
        TimeProvider timeProvider)
    {
        _retriever = retriever;
        _handleResolver = handleResolver;
        _coordinator = coordinator;
        _lifetimePolicy = lifetimePolicy;
        _timeProvider = timeProvider;
    }

    public async ValueTask<AgentMemoryReadCoreOutcome<BuildAgentMemoryPackResult>> RecallAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        BuildAgentMemoryPackInput input,
        CancellationToken cancellationToken = default)
    {
        // Validate budget — reject zero/negative before scope checks
        if (input.MaximumCount <= 0)
            throw new AgentMemoryReadCoreException("budget-invalid", "MaximumCount must be positive");
        if (input.CharacterBudget <= 0)
            throw new AgentMemoryReadCoreException("budget-invalid", "CharacterBudget must be positive");

        if (input.MaximumCount > scope.MaxRecallCount)
            throw new AgentMemoryReadCoreException("budget-invalid", "MaximumCount exceeds scope limit");
        if (input.CharacterBudget > scope.MaxRecallCharacters)
            throw new AgentMemoryReadCoreException("budget-invalid", "CharacterBudget exceeds scope limit");

        // Resolve input handles to resource IDs
        var resourceIds = new List<string>();
        if (input.MemoryHandles is { Count: > 0 })
        {
            foreach (var handleId in input.MemoryHandles)
            {
                var resolved = await _handleResolver.ResolveAsync(
                    handleId, AgentMemoryResourceKind.Memory, principal, scope, cancellationToken);
                if (resolved is null)
                    throw new AgentMemoryReadCoreException("resource-unavailable", $"Handle {handleId} not resolvable");
                resourceIds.Add(resolved.Handle.ResourceId);
            }
        }

        // Build query with visibility boundary
        var query = new AgentMemoryQuery
        {
            TenantId = principal.TenantId,
            MemoryIds = resourceIds,
            Kinds = input.Kinds
                .Select(k => MapKind(k)).ToList(),
            Tags = input.Tags,
            MaxCount = input.MaximumCount,
            CharacterBudget = input.CharacterBudget,
            MinimumConfidence = MapDomainConfidence(input.MinimumConfidence),
            IncludeStale = false,
            IncludeSuperseded = false,
            IncludeArchived = false,
            IncludeSourceRefs = true,
            VisibleDescriptorRefs = scope.VisibleDescriptorRefs,
            VisibilityBoundary = new AgentMemoryVisibilityBoundary
            {
                VisibleDescriptorRefs = scope.VisibleDescriptorRefs,
                AllowUnscopedMemory = scope.AllowUnscopedMemory
            }
        };

        // Recall
        var pack = await _retriever.RecallAsync(query, cancellationToken);

        // TenantId boundary — every memory must belong to the principal's tenant
        var visibleRefs = scope.VisibleDescriptorRefs;
        var filteredMemories = pack.Memories
            .Where(m => m.TenantId == principal.TenantId)
            .Where(m => IsVisibleInScope(m.DescriptorRefs, visibleRefs, scope.AllowUnscopedMemory))
            .ToList();

        // Create resource handles + source grants
        var now = _timeProvider.GetUtcNow();
        var handleLifetime = _lifetimePolicy.GetHandleLifetime(principal, origin, scope, "memory-pack");
        var grantLifetime = _lifetimePolicy.GetGrantLifetime(principal, origin, scope, "memory-pack");
        var scopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope);

        var handles = new List<AgentMemoryAccessResourceHandle>();
        var grants = new List<AgentMemoryAccessSourceGrant>();

        foreach (var memory in filteredMemories)
        {
            var handleId = Guid.NewGuid().ToString("N");
            handles.Add(new AgentMemoryAccessResourceHandle
            {
                HandleId = handleId,
                ResourceKind = AgentMemoryResourceKind.Memory,
                ResourceId = memory.MemoryId,
                Principal = principal,
                ScopeFingerprint = scopeFingerprint,
                RequiredDescriptorRefs = memory.DescriptorRefs,
                IsUnscoped = scope.AllowUnscopedMemory,
                IssuingOperationId = origin.OperationId,
                IssuedAt = now,
                ExpiresAt = now + handleLifetime,
            });

            if (memory.SourceRefs is { Count: > 0 })
            {
                foreach (var sourceRef in memory.SourceRefs)
                {
                    var grantId = Guid.NewGuid().ToString("N");
                    grants.Add(new AgentMemoryAccessSourceGrant
                    {
                        GrantId = grantId,
                        SourceRef = sourceRef,
                        Principal = principal,
                        ScopeFingerprint = scopeFingerprint,
                        RequiredDescriptorRefs = sourceRef.DescriptorRefs,
                        IsUnscoped = scope.AllowUnscopedMemory,
                        IssuingOperationId = origin.OperationId,
                        IssuedAt = now,
                        ExpiresAt = now + grantLifetime,
                    });
                }
            }
        }

        // Prepare artifacts via Coordinator
        var prepared = await _coordinator.PrepareAsync(
            principal, origin, scope, "memory-pack",
            preparationOrdinal: 0,
            handles: handles,
            grants: grants,
            cancellationToken);

        // Build lookups from the prepared artifacts
        var handleLookup = handles.ToDictionary(
            h => h.ResourceId, h => h.HandleId, StringComparer.Ordinal);
        var grantLookup = grants.ToDictionary(
            g => GrantKey(g), g => g, StringComparer.Ordinal);

        // Build result using Coordinator-confirmed artifacts
        var result = new BuildAgentMemoryPackResult
        {
            OperationStatus = AgentMemoryToolOperationStatus.Completed,
            Items = filteredMemories.Select(m => MapToDto(m, handleLookup, grantLookup)).ToList(),
            ReturnedCount = filteredMemories.Count,
            WasTruncated = pack.WasTruncated,
            IsAuthoritative = false,
            Diagnostics = new List<AgentMemoryToolDiagnosticDto>()
        };

        return new AgentMemoryReadCoreOutcome<BuildAgentMemoryPackResult>
        {
            Result = result,
            ScopeFingerprint = scopeFingerprint,
            MaximumAuditFacts = scope.MaxAuditFacts,
            Receipt = prepared.Receipt,
            CompensationToken = prepared.CompensationToken
        };
    }

    private static bool IsVisibleInScope(
        IReadOnlyList<DescriptorRef> memoryRefs,
        IReadOnlyList<DescriptorRef> scopeRefs,
        bool allowUnscoped)
    {
        // Unscoped memories (no descriptor refs) are visible only if explicitly allowed
        if (memoryRefs.Count == 0) return allowUnscoped;
        // Scoped memories require all their descriptor refs to be in the scope
        var scopeSet = new HashSet<DescriptorRef>(scopeRefs);
        return memoryRefs.All(r => scopeSet.Contains(r));
    }

    private static AgentMemoryKind MapKind(AgentMemoryToolKind kind) => kind switch
    {
        AgentMemoryToolKind.Preference => AgentMemoryKind.Preference,
        AgentMemoryToolKind.ProjectFact => AgentMemoryKind.ProjectFact,
        AgentMemoryToolKind.Decision => AgentMemoryKind.Decision,
        AgentMemoryToolKind.Constraint => AgentMemoryKind.Constraint,
        AgentMemoryToolKind.WorkflowHint => AgentMemoryKind.WorkflowHint,
        AgentMemoryToolKind.Risk => AgentMemoryKind.Risk,
        _ => throw new AgentMemoryReadCoreException("kind-invalid", $"Unknown memory kind: {kind}")
    };

    private static AgentMemoryConfidence MapDomainConfidence(AgentMemoryToolConfidence confidence) => confidence switch
    {
        AgentMemoryToolConfidence.Unknown => AgentMemoryConfidence.Unknown,
        AgentMemoryToolConfidence.Unspecified => AgentMemoryConfidence.Unknown,
        AgentMemoryToolConfidence.Low => AgentMemoryConfidence.Low,
        AgentMemoryToolConfidence.Medium => AgentMemoryConfidence.Medium,
        AgentMemoryToolConfidence.High => AgentMemoryConfidence.High,
        _ => throw new AgentMemoryReadCoreException("confidence-invalid", $"Unknown confidence value: {confidence}")
    };

    private static AgentMemoryToolItemDto MapToDto(
        AgentMemoryItem memory,
        IReadOnlyDictionary<string, string> handleLookup,
        IReadOnlyDictionary<string, AgentMemoryAccessSourceGrant> grantLookup)
    {
        return new AgentMemoryToolItemDto
        {
            MemoryHandle = handleLookup.GetValueOrDefault(memory.MemoryId, string.Empty),
            Kind = MapToolKind(memory.Kind),
            Content = memory.Content,
            CanonicalContentHash = new AgentMemoryToolCanonicalHashDto
            {
                Value = memory.CanonicalContentHash.Value,
                AlgorithmVersion = memory.CanonicalContentHash.AlgorithmVersion,
                ContractVersion = memory.CanonicalContentHash.ContractVersion,
                CanonicalShapeVersion = memory.CanonicalContentHash.CanonicalShapeVersion
            },
            Confidence = MapConfidence(memory.Confidence),
            MemoryStatus = memory.Status switch
            {
                AgentMemoryStatus.Active => AgentMemoryToolMemoryStatus.Active,
                AgentMemoryStatus.Superseded => AgentMemoryToolMemoryStatus.Superseded,
                AgentMemoryStatus.Archived => AgentMemoryToolMemoryStatus.Archived,
                _ => AgentMemoryToolMemoryStatus.Unknown
            },
            IsAuthoritative = memory.IsAuthoritative,
            Tags = memory.Tags,
            SourceGrants = memory.SourceRefs.Select(s =>
            {
                grantLookup.TryGetValue(
                    $"{s.SourceId}:{s.RangeStart}:{s.RangeEnd}",
                    out var grant);
                return new AgentMemorySourceGrantDto
                {
                    GrantId = grant?.GrantId ?? string.Empty,
                    SourceKind = MapSourceKind(s.SourceKind),
                    ExpiresAt = grant?.ExpiresAt ?? DateTimeOffset.MaxValue,
                };
            }).ToList()
        };
    }

    private static AgentMemoryToolKind MapToolKind(AgentMemoryKind kind) => kind switch
    {
        AgentMemoryKind.Preference => AgentMemoryToolKind.Preference,
        AgentMemoryKind.ProjectFact => AgentMemoryToolKind.ProjectFact,
        AgentMemoryKind.Decision => AgentMemoryToolKind.Decision,
        AgentMemoryKind.Constraint => AgentMemoryToolKind.Constraint,
        AgentMemoryKind.WorkflowHint => AgentMemoryToolKind.WorkflowHint,
        AgentMemoryKind.Risk => AgentMemoryToolKind.Risk,
        _ => AgentMemoryToolKind.Unknown
    };

    private static AgentMemoryToolConfidence MapConfidence(AgentMemoryConfidence confidence) => confidence switch
    {
        AgentMemoryConfidence.High => AgentMemoryToolConfidence.High,
        AgentMemoryConfidence.Medium => AgentMemoryToolConfidence.Medium,
        AgentMemoryConfidence.Low => AgentMemoryToolConfidence.Low,
        AgentMemoryConfidence.Unknown => AgentMemoryToolConfidence.Unknown,
        _ => AgentMemoryToolConfidence.Unknown
    };

    private static AgentMemoryToolSourceKind MapSourceKind(AgentSourceKind kind) => kind switch
    {
        AgentSourceKind.ConversationTurn => AgentMemoryToolSourceKind.ConversationTurn,
        AgentSourceKind.TaskRecord => AgentMemoryToolSourceKind.TaskRecord,
        AgentSourceKind.TaskEvent => AgentMemoryToolSourceKind.TaskEvent,
        AgentSourceKind.CompressedContextBlock => AgentMemoryToolSourceKind.CompressedContextBlock,
        AgentSourceKind.MemoryCandidate => AgentMemoryToolSourceKind.MemoryCandidate,
        AgentSourceKind.MemoryItem => AgentMemoryToolSourceKind.MemoryItem,
        AgentSourceKind.MetadataContextPack => AgentMemoryToolSourceKind.MetadataContextPack,
        AgentSourceKind.ReviewReport => AgentMemoryToolSourceKind.ReviewReport,
        AgentSourceKind.FixProposal => AgentMemoryToolSourceKind.FixProposal,
        AgentSourceKind.PackagePreview => AgentMemoryToolSourceKind.PackagePreview,
        AgentSourceKind.ActivationRequest => AgentMemoryToolSourceKind.ActivationRequest,
        _ => AgentMemoryToolSourceKind.Unknown
    };

    private static string GrantKey(AgentMemoryAccessSourceGrant grant)
    {
        return $"{grant.SourceRef.SourceId}:{grant.SourceRef.RangeStart}:{grant.SourceRef.RangeEnd}";
    }
}
