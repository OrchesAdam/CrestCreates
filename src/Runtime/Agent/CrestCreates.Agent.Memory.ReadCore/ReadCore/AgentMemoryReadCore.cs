using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Agent.Memory.ReadCore.Accountability;

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
    private readonly IAgentMemoryAccountabilityProducer _producer;
    private readonly AgentMemoryEffectiveResultHashProjector _effectiveResultHashProjector;

    public AgentMemoryReadCore(
        IAgentMemoryRetriever retriever,
        IAgentMemoryAccessHandleResolver handleResolver,
        IAgentMemoryAccessArtifactCoordinator coordinator,
        IAgentMemoryArtifactLifetimePolicy lifetimePolicy,
        IAgentMemoryCurrentClosureProvider closureProvider,
        TimeProvider timeProvider,
        IAgentMemoryAccountabilityProducer producer,
        AgentMemoryEffectiveResultHashProjector effectiveResultHashProjector)
    {
        _retriever = retriever;
        _handleResolver = handleResolver;
        _coordinator = coordinator;
        _lifetimePolicy = lifetimePolicy;
        _closureProvider = closureProvider;
        _timeProvider = timeProvider;
        _producer = producer;
        _effectiveResultHashProjector = effectiveResultHashProjector;
    }

    public async ValueTask<AgentMemoryReadCoreOutcome<BuildAgentMemoryPackResult>> RecallAsync(
        AgentMemoryRecallOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        AgentMemoryOperationRequestValidator.Validate(
            request.Principal, request.Scope, request.Identity, request.InvocationContext, request.Origin);
        return await RecallCoreAsync(request, cancellationToken);
    }

    private async ValueTask<AgentMemoryReadCoreOutcome<BuildAgentMemoryPackResult>> RecallCoreAsync(
        AgentMemoryRecallOperationRequest request,
        CancellationToken cancellationToken)
    {
        var principal = request.Principal;
        var origin = request.Origin;
        var scope = request.Scope;
        var input = request.Input;

        // Validate budget — reject zero/negative before scope checks
        if (input.MaximumCount <= 0)
            await RejectRecallAsync(request, "budget-invalid", "MaximumCount must be positive");
        if (input.CharacterBudget <= 0)
            await RejectRecallAsync(request, "budget-invalid", "CharacterBudget must be positive");

        if (input.MaximumCount > scope.MaxRecallCount)
            await RejectRecallAsync(request, "budget-invalid", "MaximumCount exceeds scope limit");
        if (input.CharacterBudget > scope.MaxRecallCharacters)
            await RejectRecallAsync(request, "budget-invalid", "CharacterBudget exceeds scope limit");

        // Resolve input handles to resource IDs
        var resourceIds = new List<string>();
        if (input.MemoryHandles is { Count: > 0 })
        {
            foreach (var handleId in input.MemoryHandles)
            {
                var resolved = await _handleResolver.ResolveAsync(
                    handleId, AgentMemoryResourceKind.Memory, principal, scope, cancellationToken);
                if (resolved is null)
                    await RejectRecallAsync(request, "resource-unavailable", "Memory Handle is not resolvable");
                resourceIds.Add(resolved!.Handle.ResourceId);
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

        var handlePlan = new Dictionary<string, AgentMemoryAccessResourceHandle>(StringComparer.Ordinal);
        var grantPlan = new Dictionary<AgentMemorySourceKey, AgentMemoryAccessSourceGrant>();
        var memorySourceKeys = new List<(string MemoryId, List<AgentMemorySourceKey> SourceKeys)>();

        foreach (var memory in filteredMemories)
        {
            var handleId = Guid.NewGuid().ToString("N");
            var effectiveClosure = EffectiveClosureHelper.ComputeEffectiveClosure(memory.DescriptorRefs, memory.SourceRefs);
            var plannedHandle = new AgentMemoryAccessResourceHandle
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
            };
            if (!handlePlan.TryAdd(memory.MemoryId, plannedHandle))
            {
                throw new AgentMemoryReadCoreException(
                    "handle-contract",
                    $"Recall returned duplicate MemoryId {memory.MemoryId}");
            }

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

                    var requiredDescriptorRefs = AgentMemoryHandleGrantMatrix.GetRequiredDescriptorRefs(
                        sourceRef.SourceKind,
                        sourceClosure.CurrentDescriptorRefs);
                    var isUnscoped = AgentMemoryHandleGrantMatrix.IsUnscopedGrant(
                        sourceRef.SourceKind,
                        requiredDescriptorRefs);

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
            handles: handlePlan.Values.ToList(),
            grants: grants,
            cancellationToken);

        try
        {
            var confirmedByKey = AgentMemoryPreparedArtifactContractVerifier.VerifyGrants(
                grantPlan,
                prepared.Grants?.Grants);
            var confirmedHandlesByResourceId = AgentMemoryPreparedArtifactContractVerifier.VerifyHandles(
                handlePlan,
                prepared.Handles?.Handles);

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
                        MemoryHandle = confirmedHandlesByResourceId[m.MemoryId].HandleId,
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

            // Post-result fence: once the terminal Memory result is established,
            // all Accountability work is best-effort. No exception from this fence
            // may replace the established result or trigger the compensation
            // token revoke in the outer catch.
            try
            {
                await PublishCompletedRecallFactAsync(request, result);
            }
            catch
            {
                // Swallow: an Accountability failure must not change the Recall result.
            }

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

    private async ValueTask PublishCompletedRecallFactAsync(
        AgentMemoryRecallOperationRequest request,
        BuildAgentMemoryPackResult result)
    {
        var effectiveHashes = new List<CanonicalHash>(result.Items.Count);
        foreach (var item in result.Items)
        {
            effectiveHashes.Add(_effectiveResultHashProjector.ComputeEffectiveVisibleContentHash(
                request.Principal.TenantId,
                item.Content));
        }

        var requestedKinds = AgentMemoryEffectiveResultHashProjector.MapRequestedKinds(request.Input.Kinds);
        var minimumConfidence = AgentMemoryEffectiveResultHashProjector.MapMinimumConfidence(request.Input.MinimumConfidence);

        var effectivePackHash = _effectiveResultHashProjector.ComputeEffectivePackHash(
            request.Principal.TenantId,
            effectiveHashes,
            result.ReturnedCount,
            result.WasTruncated,
            requestedKinds,
            request.Input.MaximumCount,
            request.Input.CharacterBudget,
            minimumConfidence);

        var payload = new AgentMemoryRecallAccountabilityPayload
        {
            OperationId = request.Identity.OperationId,
            Result = "completed",
            EffectivePackHash = effectivePackHash,
            ReturnedCount = result.ReturnedCount,
            WasTruncated = result.WasTruncated,
            RequestedKinds = requestedKinds,
            MaximumCount = request.Input.MaximumCount,
            CharacterBudget = request.Input.CharacterBudget,
            MinimumConfidence = minimumConfidence
        };

        await _producer.PublishRecallAsync(request.Identity, request.InvocationContext, payload);
    }

    private async ValueTask PublishRejectedRecallFactAsync(
        AgentMemoryRecallOperationRequest request,
        string failureCode)
    {
        try
        {
            var payload = new AgentMemoryRecallAccountabilityPayload
            {
                OperationId = request.Identity.OperationId,
                Result = "rejected",
                StableFailureCode = failureCode,
                ReturnedCount = 0,
                WasTruncated = false,
                RequestedKinds = AgentMemoryEffectiveResultHashProjector.MapRequestedKinds(request.Input.Kinds),
                MaximumCount = request.Input.MaximumCount,
                CharacterBudget = request.Input.CharacterBudget,
                MinimumConfidence = AgentMemoryEffectiveResultHashProjector.MapMinimumConfidence(request.Input.MinimumConfidence)
            };

            await _producer.PublishRecallAsync(request.Identity, request.InvocationContext, payload);
        }
        catch
        {
            // Swallow: publishing a rejected fact must never change the original exception.
        }
    }

    private async ValueTask RejectRecallAsync(
        AgentMemoryRecallOperationRequest request,
        string failureCode,
        string message)
    {
        await PublishRejectedRecallFactAsync(request, failureCode);
        throw new AgentMemoryReadCoreException(failureCode, message);
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
