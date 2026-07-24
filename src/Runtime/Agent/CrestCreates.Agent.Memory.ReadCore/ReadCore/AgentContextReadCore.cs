using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Projection.Security;
using CrestCreates.Agent.Memory.Projection.Abstractions.Security;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.ReadCore;

/// <summary>
/// Shared context recall core. Protocol-neutral.
/// Resolves a context handle, selects blocks within budget/range,
/// and issues source grants per block for ctx_expand follow-up.
/// </summary>
internal sealed class AgentContextReadCore : IAgentContextReadCore
{
    private readonly IAgentMemoryAccessHandleResolver _handleResolver;
    private readonly IAgentCompressedContextStore _contextStore;
    private readonly IAgentMemoryAccessArtifactCoordinator _coordinator;
    private readonly IAgentMemoryArtifactLifetimePolicy _lifetimePolicy;
    private readonly IAgentMemoryCurrentClosureProvider _closureProvider;
    private readonly TimeProvider _timeProvider;

    public AgentContextReadCore(
        IAgentMemoryAccessHandleResolver handleResolver,
        IAgentCompressedContextStore contextStore,
        IAgentMemoryAccessArtifactCoordinator coordinator,
        IAgentMemoryArtifactLifetimePolicy lifetimePolicy,
        IAgentMemoryCurrentClosureProvider closureProvider,
        TimeProvider timeProvider)
    {
        _handleResolver = handleResolver;
        _contextStore = contextStore;
        _coordinator = coordinator;
        _lifetimePolicy = lifetimePolicy;
        _closureProvider = closureProvider;
        _timeProvider = timeProvider;
    }

    public async ValueTask<AgentMemoryReadCoreOutcome<RecallAgentContextResult>> RecallContextAsync(
        AgentMemoryAccessPrincipal principal,
        AgentMemoryArtifactOrigin origin,
        AgentMemoryAccessScope scope,
        RecallAgentContextInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateBudget(input, scope);

        var resolved = await _handleResolver.ResolveAsync(
            input.ContextHandle, AgentMemoryResourceKind.Context, principal, scope, cancellationToken);
        if (resolved is null)
            throw new AgentMemoryReadCoreException("resource-unavailable", "Context handle not resolvable");

        var context = await _contextStore.GetCompressedContextAsync(
            principal.TenantId, resolved.Handle.ResourceId, cancellationToken);
        if (context is null)
            throw new AgentMemoryReadCoreException("resource-unavailable", "Context not found");

        if (!string.Equals(context.TenantId, principal.TenantId, StringComparison.Ordinal))
            throw new AgentMemoryReadCoreException("resource-unavailable", "Context tenant mismatch");

        var allBlocks = context.Blocks ?? Array.Empty<AgentCompressedContextBlock>();
        var selectedBlocks = SelectBlocks(allBlocks, input);

        // Apply character budget: consume blocks in order, truncate final block if needed
        var budgetedBlocks = ApplyCharacterBudget(selectedBlocks, input.CharacterBudget, out var wasTruncated);

        // Apply block count limit
        if (budgetedBlocks.Count > input.MaximumBlockCount)
        {
            budgetedBlocks = budgetedBlocks.Take(input.MaximumBlockCount).ToList();
            wasTruncated = true;
        }

        // Build unique grant plan: one grant per unique SourceKey, regardless of how many
        // blocks reference it. This prevents orphaned grants and quota exhaustion.
        var now = _timeProvider.GetUtcNow();
        var grantLifetime = _lifetimePolicy.GetGrantLifetime(principal, origin, scope, "ctx-recall");
        var scopeFingerprint = AgentMemoryScopeFingerprint.Compute(scope);

        var grantPlan = new Dictionary<AgentMemorySourceKey, AgentMemoryAccessSourceGrant>();
        var blockSourceKeys = new List<(int BlockIndex, List<AgentMemorySourceKey> SourceKeys)>();

        for (var i = 0; i < budgetedBlocks.Count; i++)
        {
            var block = budgetedBlocks[i];
            var keys = new List<AgentMemorySourceKey>();

            if (block.SourceRefs is { Count: > 0 })
            {
                foreach (var sourceRef in block.SourceRefs)
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

            blockSourceKeys.Add((i, keys));
        }

        // Prepare artifacts via Coordinator — one grant per unique SourceKey
        var grants = grantPlan.Values.ToList();
        var prepared = await _coordinator.PrepareAsync(
            principal, origin, scope, "ctx-recall",
            preparationOrdinal: 0,
            handles: Array.Empty<AgentMemoryAccessResourceHandle>(),
            grants: grants,
            cancellationToken);

        try
        {
            var confirmedByKey = AgentMemoryPreparedArtifactContractVerifier.VerifyGrants(
                grantPlan,
                prepared.Grants?.Grants);

            // Map blocks to DTOs with Coordinator-confirmed grants
            var sourceKeyToGrantDto = new Dictionary<AgentMemorySourceKey, AgentMemorySourceGrantDto>();
            var blockDtos = new List<AgentMemoryToolBlockDto>();

            for (var i = 0; i < budgetedBlocks.Count; i++)
            {
                var block = budgetedBlocks[i];
                var mapping = blockSourceKeys.FirstOrDefault(m => m.BlockIndex == i);
                var confirmedBlockGrants = new List<AgentMemorySourceGrantDto>();

                foreach (var sourceKey in mapping.SourceKeys)
                {
                    if (sourceKeyToGrantDto.TryGetValue(sourceKey, out var sharedDto))
                    {
                        confirmedBlockGrants.Add(sharedDto);
                        continue;
                    }

                    var confirmedGrant = confirmedByKey[sourceKey];
                    var dto = new AgentMemorySourceGrantDto
                    {
                        GrantId = confirmedGrant.GrantId,
                        SourceKind = MapSourceKind(sourceKey.SourceKind),
                        ExpiresAt = confirmedGrant.ExpiresAt,
                    };
                    sourceKeyToGrantDto[sourceKey] = dto;
                    confirmedBlockGrants.Add(dto);
                }

                blockDtos.Add(new AgentMemoryToolBlockDto
                {
                    Content = block.Content ?? string.Empty,
                    CanonicalContentHash = block.CanonicalContentHash is not null
                        ? new AgentMemoryToolCanonicalHashDto
                        {
                            Value = block.CanonicalContentHash.Value,
                            AlgorithmVersion = block.CanonicalContentHash.AlgorithmVersion,
                            ContractVersion = block.CanonicalContentHash.ContractVersion,
                            CanonicalShapeVersion = block.CanonicalContentHash.CanonicalShapeVersion
                        }
                        : new AgentMemoryToolCanonicalHashDto
                        {
                            Value = string.Empty,
                            AlgorithmVersion = "v1",
                            ContractVersion = "v1",
                            CanonicalShapeVersion = "v1"
                        },
                    SourceGrants = confirmedBlockGrants
                });
            }

            var result = new RecallAgentContextResult
            {
                OperationStatus = AgentMemoryToolOperationStatus.Completed,
                WasTruncated = wasTruncated,
                BlockCount = allBlocks.Count,
                Blocks = blockDtos,
                Diagnostics = new List<AgentMemoryToolDiagnosticDto>()
            };

            return new AgentMemoryReadCoreOutcome<RecallAgentContextResult>
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

    private static void ValidateBudget(RecallAgentContextInput input, AgentMemoryAccessScope scope)
    {
        if (input.CharacterBudget <= 0)
            throw new AgentMemoryReadCoreException("budget-invalid", "CharacterBudget must be positive");
        if (input.CharacterBudget > scope.MaxContextRecallCharacters)
            throw new AgentMemoryReadCoreException("budget-invalid", "CharacterBudget exceeds scope limit");
        if (input.MaximumBlockCount <= 0)
            throw new AgentMemoryReadCoreException("budget-invalid", "MaximumBlockCount must be positive");
        if (input.MaximumBlockCount > scope.MaxCompressedBlockCount)
            throw new AgentMemoryReadCoreException("budget-invalid", "MaximumBlockCount exceeds scope limit");
        if (input.StartBlockIndex < 0)
            throw new AgentMemoryReadCoreException("budget-invalid", "StartBlockIndex must be non-negative");
        if (input.EndBlockIndexExclusive.HasValue)
        {
            if (input.EndBlockIndexExclusive.Value <= input.StartBlockIndex)
                throw new AgentMemoryReadCoreException("budget-invalid", "EndBlockIndexExclusive must be greater than StartBlockIndex");
            if (input.EndBlockIndexExclusive.Value - input.StartBlockIndex > scope.MaxCompressedBlockCount)
                throw new AgentMemoryReadCoreException("budget-invalid", "Block range span exceeds scope limit");
        }
    }

    private static List<AgentCompressedContextBlock> SelectBlocks(
        IReadOnlyList<AgentCompressedContextBlock> allBlocks,
        RecallAgentContextInput input)
    {
        var start = input.StartBlockIndex;
        var end = input.EndBlockIndexExclusive ?? allBlocks.Count;

        if (start >= allBlocks.Count)
            return new List<AgentCompressedContextBlock>();

        if (end > allBlocks.Count)
            end = allBlocks.Count;

        return allBlocks.Skip(start).Take(end - start).ToList();
    }

    private static List<AgentCompressedContextBlock> ApplyCharacterBudget(
        List<AgentCompressedContextBlock> blocks,
        int characterBudget,
        out bool wasTruncated)
    {
        wasTruncated = false;
        var result = new List<AgentCompressedContextBlock>();
        var remaining = characterBudget;

        foreach (var block in blocks)
        {
            var content = block.Content ?? string.Empty;
            if (remaining <= 0)
            {
                wasTruncated = true;
                break;
            }

            if (content.Length <= remaining)
            {
                result.Add(block);
                remaining -= content.Length;
            }
            else
            {
                result.Add(block with { Content = content[..remaining] });
                remaining = 0;
                wasTruncated = true;
            }
        }

        return result;
    }

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
