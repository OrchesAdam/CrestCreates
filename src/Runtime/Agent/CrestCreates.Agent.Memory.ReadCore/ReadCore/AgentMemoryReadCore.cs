using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
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
    private readonly IAgentMemoryCurrentClosureProvider _closureProvider;
    private readonly TimeProvider _timeProvider;

    public AgentMemoryReadCore(
        IAgentMemoryRetriever retriever,
        IAgentMemoryAccessHandleResolver handleResolver,
        IAgentMemoryAccessArtifactCoordinator coordinator,
        IAgentMemoryArtifactLifetimePolicy lifetimePolicy,
        IAgentMemoryCurrentClosureProvider closureProvider,
        TimeProvider timeProvider)
    {
        _retriever = retriever;
        _handleResolver = handleResolver;
        _coordinator = coordinator;
        _lifetimePolicy = lifetimePolicy;
        _closureProvider = closureProvider;
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

        // Pack TenantId boundary — the entire pack must belong to the principal's tenant.
        // Individual item filtering is insufficient: a foreign-tenant pack containing
        // local-tenant items would pass item-level checks but violate pack-level integrity.
        if (!string.Equals(pack.TenantId, principal.TenantId, StringComparison.Ordinal))
            throw new AgentMemoryReadCoreException("tenant-boundary", "Pack TenantId does not match principal TenantId");

        // TenantId boundary — every memory must belong to the principal's tenant
        var visibleRefs = scope.VisibleDescriptorRefs;
        var filteredMemories = pack.Memories
            .Where(m => m.TenantId == principal.TenantId)
            .Where(m =>
            {
                var effectiveClosure = EffectiveClosureHelper.ComputeEffectiveClosure(m.DescriptorRefs, m.SourceRefs);
                return IsVisibleInScope(effectiveClosure, visibleRefs, scope.AllowUnscopedMemory);
            })
            .ToList();

        // Create resource handles + source grants (deduplicated by unique SourceKey)
        var now = _timeProvider.GetUtcNow();
        var handleLifetime = _lifetimePolicy.GetHandleLifetime(principal, origin, scope, "memory-pack");
        var grantLifetime = _lifetimePolicy.GetGrantLifetime(principal, origin, scope, "memory-pack");
        var scopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope);

        var handles = new List<AgentMemoryAccessResourceHandle>();
        var grantPlan = new Dictionary<AgentMemorySourceKey, AgentMemoryAccessSourceGrant>();
        var memorySourceKeys = new List<(string MemoryId, List<AgentMemorySourceKey> SourceKeys)>();

        foreach (var memory in filteredMemories)
        {
            var handleId = Guid.NewGuid().ToString("N");
            var effectiveClosure = EffectiveClosureHelper.ComputeEffectiveClosure(memory.DescriptorRefs, memory.SourceRefs);
            handles.Add(new AgentMemoryAccessResourceHandle
            {
                HandleId = handleId,
                ResourceKind = AgentMemoryResourceKind.Memory,
                ResourceId = memory.MemoryId,
                Principal = principal,
                ScopeFingerprint = scopeFingerprint,
                RequiredDescriptorRefs = effectiveClosure,
                IsUnscoped = effectiveClosure.Count == 0,
                IssuingOperationId = origin.OperationId,
                IssuedAt = now,
                ExpiresAt = now + handleLifetime,
            });

            var keys = new List<AgentMemorySourceKey>();

            if (memory.SourceRefs is { Count: > 0 })
            {
                foreach (var sourceRef in memory.SourceRefs)
                {
                    if (!AgentMemorySourceKindSupport.IsGrantSupported(sourceRef.SourceKind))
                        continue;

                    if (!string.Equals(sourceRef.TenantId, principal.TenantId, StringComparison.Ordinal))
                        continue;

                    if (!AgentMemoryHandleGrantMatrix.IsRangeAllowed(sourceRef))
                        continue;

                    AgentMemoryResourceKind sourceResourceKind;
                    try
                    {
                        sourceResourceKind = AgentMemorySourceKindSupport.ToResourceKind(sourceRef.SourceKind);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }

                    var sourceKey = new AgentMemorySourceKey(
                        sourceRef.TenantId, sourceRef.SourceKind, sourceRef.SourceId,
                        sourceRef.RangeStart, sourceRef.RangeEnd);

                    if (grantPlan.TryGetValue(sourceKey, out var existingGrant))
                    {
                        var existingDescs = existingGrant.SourceRef.DescriptorRefs ?? Array.Empty<DescriptorRef>();
                        var newDescs = sourceRef.DescriptorRefs ?? Array.Empty<DescriptorRef>();
                        if (existingDescs.Count != newDescs.Count
                            || !new HashSet<DescriptorRef>(existingDescs).SetEquals(newDescs))
                            throw new AgentMemoryReadCoreException("conflicting-source-descriptors",
                                $"SourceKey {sourceKey} has conflicting DescriptorRefs across references");

                        keys.Add(sourceKey);
                        continue;
                    }

                    var sourceClosure = await _closureProvider.GetCurrentClosureAsync(
                        sourceResourceKind, principal.TenantId, sourceRef.SourceId,
                        sourceRef: sourceRef, cancellationToken: cancellationToken);

                    if (sourceClosure is null) continue;
                    if (!string.Equals(sourceClosure.TenantId, principal.TenantId, StringComparison.Ordinal)) continue;

                    var sourceClosureRefs = sourceClosure.CurrentDescriptorRefs;
                    var scopeBinding = AgentMemoryHandleGrantMatrix.GetScopeBinding(sourceRef.SourceKind);
                    var isUnscoped = scopeBinding == AgentMemoryHandleGrantMatrix.GrantScopeBinding.DescriptorBound
                        && sourceClosureRefs.Count == 0;

                    var closurePolicy = AgentMemoryHandleGrantMatrix.GetClosurePolicy(sourceRef.SourceKind);
                    var requiredDescriptorRefs = closurePolicy == AgentMemoryHandleGrantMatrix.GrantClosurePolicy.Exact
                        ? sourceClosureRefs
                        : Array.Empty<DescriptorRef>();

                    grantPlan[sourceKey] = new AgentMemoryAccessSourceGrant
                    {
                        GrantId = Guid.NewGuid().ToString("N"),
                        SourceRef = sourceRef,
                        Principal = principal,
                        ScopeFingerprint = scopeFingerprint,
                        RequiredDescriptorRefs = requiredDescriptorRefs,
                        IsUnscoped = isUnscoped,
                        IssuingOperationId = origin.OperationId,
                        IssuedAt = now,
                        ExpiresAt = now + grantLifetime,
                    };

                    keys.Add(sourceKey);
                }
            }

            memorySourceKeys.Add((memory.MemoryId, keys));
        }

        // Prepare artifacts via Coordinator — one grant per unique SourceKey
        var grants = grantPlan.Values.ToList();
        var prepared = await _coordinator.PrepareAsync(
            principal, origin, scope, "memory-pack",
            preparationOrdinal: 0,
            handles: handles,
            grants: grants,
            cancellationToken);

        try
        {
            // Build confirmed grant lookup by SourceKey
            var confirmedGrants = prepared.Grants?.Grants ?? [];
            var confirmedByKey = new Dictionary<AgentMemorySourceKey, AgentMemoryAccessSourceGrant>();
            foreach (var g in confirmedGrants)
            {
                if (g is null) continue;
                var key = new AgentMemorySourceKey(
                    g.SourceRef.TenantId, g.SourceRef.SourceKind, g.SourceRef.SourceId,
                    g.SourceRef.RangeStart, g.SourceRef.RangeEnd);
                if (confirmedByKey.ContainsKey(key))
                    throw new AgentMemoryReadCoreException("grant-contract",
                        $"Coordinator returned duplicate confirmed grant for SourceKey {key}");
                confirmedByKey[key] = g;
            }

            // Contract: every requested SourceKey must have a confirmed grant
            foreach (var requestedKey in grantPlan.Keys)
            {
                if (!confirmedByKey.ContainsKey(requestedKey))
                    throw new AgentMemoryReadCoreException("grant-contract",
                        $"Coordinator did not confirm grant for SourceKey {requestedKey}");
            }

            // Contract: no extra confirmed grants beyond the plan
            foreach (var confirmedKey in confirmedByKey.Keys)
            {
                if (!grantPlan.ContainsKey(confirmedKey))
                    throw new AgentMemoryReadCoreException("grant-contract",
                        $"Coordinator returned unexpected confirmed grant for SourceKey {confirmedKey}");
            }

            // Build handle lookup from confirmed artifacts
            var handleLookup = (prepared.Handles?.Handles ?? [])
                .Where(h => h is not null)
                .ToDictionary(h => h.ResourceId, h => h.HandleId, StringComparer.Ordinal);

            // Build grant DTO lookup by SourceKey for reuse across memories
            var grantDtoLookup = new Dictionary<AgentMemorySourceKey, AgentMemorySourceGrantDto>();

            // Build result using Coordinator-confirmed artifacts
            var result = new BuildAgentMemoryPackResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.Completed,
                Items = filteredMemories.Select(m =>
                {
                    var entry = memorySourceKeys.FirstOrDefault(e => e.MemoryId == m.MemoryId);
                    var sourceGrants = new List<AgentMemorySourceGrantDto>();
                    foreach (var sourceKey in entry.SourceKeys)
                    {
                        if (grantDtoLookup.TryGetValue(sourceKey, out var sharedDto))
                        {
                            sourceGrants.Add(sharedDto);
                            continue;
                        }

                        var confirmedGrant = confirmedByKey[sourceKey];
                        var dto = new AgentMemorySourceGrantDto
                        {
                            GrantId = confirmedGrant.GrantId,
                            SourceKind = MapSourceKind(sourceKey.SourceKind),
                            ExpiresAt = confirmedGrant.ExpiresAt,
                        };
                        grantDtoLookup[sourceKey] = dto;
                        sourceGrants.Add(dto);
                    }

                    return new AgentMemoryToolItemDto
                    {
                        MemoryHandle = handleLookup.GetValueOrDefault(m.MemoryId, string.Empty),
                        Kind = MapToolKind(m.Kind),
                        Content = m.Content,
                        CanonicalContentHash = new AgentMemoryToolCanonicalHashDto
                        {
                            Value = m.CanonicalContentHash.Value,
                            AlgorithmVersion = m.CanonicalContentHash.AlgorithmVersion,
                            ContractVersion = m.CanonicalContentHash.ContractVersion,
                            CanonicalShapeVersion = m.CanonicalContentHash.CanonicalShapeVersion
                        },
                        Confidence = MapConfidence(m.Confidence),
                        MemoryStatus = m.Status switch
                        {
                            AgentMemoryStatus.Active => AgentMemoryToolMemoryStatus.Active,
                            AgentMemoryStatus.Superseded => AgentMemoryToolMemoryStatus.Superseded,
                            AgentMemoryStatus.Archived => AgentMemoryToolMemoryStatus.Archived,
                            _ => AgentMemoryToolMemoryStatus.Unknown
                        },
                        IsAuthoritative = false,
                        Tags = m.Tags,
                        SourceGrants = sourceGrants
                    };
                }).ToList(),
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
        catch
        {
            if (prepared.CompensationToken is not null)
                await _coordinator.RevokeCreatedAsync(prepared.CompensationToken, CancellationToken.None);
            throw;
        }
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
}
