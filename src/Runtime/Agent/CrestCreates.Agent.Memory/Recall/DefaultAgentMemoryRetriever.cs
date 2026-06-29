using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.CanonicalHashing;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Recall;

public sealed class DefaultAgentMemoryRetriever : IAgentMemoryRetriever
{
    private readonly IAgentMemoryStore _store;
    private readonly AgentMemoryCanonicalHashProjector _hashProjector;

    public DefaultAgentMemoryRetriever(IAgentMemoryStore store, AgentMemoryCanonicalHashProjector hashProjector)
    {
        _store = store;
        _hashProjector = hashProjector;
    }

    public async ValueTask<AgentMemoryPack> RecallAsync(AgentMemoryQuery query, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<AgentMemoryDiagnostic>();

        // Build a store-level query with only persistence-level fields
        var storeQuery = new AgentMemoryQuery
        {
            TenantId = query.TenantId,
            Kinds = query.Kinds,
            Tags = query.Tags,
            MemoryIds = query.MemoryIds,
            IncludeStale = query.IncludeStale,
            IncludeSuperseded = query.IncludeSuperseded,
            IncludeArchived = query.IncludeArchived,
            DescriptorRefs = query.DescriptorRefs
        };

        var memories = await _store.ListMemoriesAsync(storeQuery, cancellationToken);

        // Apply deterministic ordering BEFORE recall-level filtering
        var ordered = memories
            .OrderByDescending(m => m.Confidence)
            .ThenBy(m => m.Kind)
            .ThenByDescending(m => m.PromotedAt)
            .ThenBy(m => m.MemoryId)
            .ThenBy(m => m.CanonicalContentHash.Value)
            .ToArray();

        // Apply recall-level filtering
        var filtered = FilterMemories(ordered, query, diagnostics);

        // Capture eligible memory IDs before budget truncation (for visible memory set hash)
        var eligibleSortedMemoryIds = filtered
            .Select(m => m.MemoryId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        // Compute scope fingerprint from query
        var scopeFingerprint = _hashProjector.ComputeScopeFingerprint(query);

        // Compute visible memory set hash from sorted eligible memory IDs
        var visibleMemorySetHash = _hashProjector.ComputeVisibleMemorySetHash(eligibleSortedMemoryIds);

        // Apply character budget
        var (budgetedMemories, wasTruncated) = ApplyCharacterBudget(filtered, query.CharacterBudget);

        if (wasTruncated)
        {
            diagnostics.Add(new AgentMemoryDiagnostic
            {
                Code = AgentMemoryDiagnosticCodes.BudgetTruncated,
                Message = $"Memory recall budget truncated. {filtered.Count - budgetedMemories.Count} memory/memories omitted.",
                Severity = SeverityLevel.Warning
            });
        }

        // Compute pack hash from final included memories' canonical content hashes
        var sortedMemoryHashes = budgetedMemories
            .Select(m => m.CanonicalContentHash)
            .OrderBy(h => h.Value, StringComparer.Ordinal)
            .ToArray();
        var canonicalPackHash = _hashProjector.ComputePackHash(
            query.TenantId,
            scopeFingerprint,
            visibleMemorySetHash,
            sortedMemoryHashes);

        return new AgentMemoryPack
        {
            TenantId = query.TenantId,
            Memories = budgetedMemories,
            Diagnostics = diagnostics.ToArray(),
            IsAuthoritative = false,
            ScopeFingerprint = scopeFingerprint,
            VisibleMemorySetHash = visibleMemorySetHash,
            CanonicalPackHash = canonicalPackHash
        };
    }

    private static IReadOnlyList<AgentMemoryItem> FilterMemories(
        IReadOnlyList<AgentMemoryItem> memories,
        AgentMemoryQuery query,
        List<AgentMemoryDiagnostic> diagnostics)
    {
        // VisibleDescriptorKinds filter — fail closed: DescriptorRef doesn't carry DescriptorKind
        if (query.VisibleDescriptorKinds.Count > 0)
        {
            diagnostics.Add(new AgentMemoryDiagnostic
            {
                Code = AgentMemoryDiagnosticCodes.VisibilityKindUnresolvable,
                Message = "VisibleDescriptorKinds filter was supplied but cannot be evaluated: DescriptorRef does not carry DescriptorKind. Returning empty results for safety.",
                Severity = SeverityLevel.Warning
            });
            return Array.Empty<AgentMemoryItem>();
        }

        var result = new List<AgentMemoryItem>();

        foreach (var memory in memories)
        {
            // MinimumConfidence filter
            if (memory.Confidence < query.MinimumConfidence)
                continue;

            // VisibleDescriptorRefs filter
            if (query.VisibleDescriptorRefs.Count > 0)
            {
                var hasVisibleRef = query.VisibleDescriptorRefs.Any(qr =>
                    memory.DescriptorRefs.Any(mr => mr.Equals(qr)));
                if (!hasVisibleRef)
                    continue;
            }

            result.Add(memory);

            // MaxCount filter
            if (query.MaxCount.HasValue && result.Count >= query.MaxCount.Value)
                break;
        }

        return result.ToArray();
    }

    private static (IReadOnlyList<AgentMemoryItem> Memories, bool WasTruncated) ApplyCharacterBudget(IReadOnlyList<AgentMemoryItem> memories, int? characterBudget)
    {
        if (characterBudget is not { } budget)
            return (memories, false);

        var result = new List<AgentMemoryItem>();
        var used = 0;

        foreach (var memory in memories)
        {
            if (used + memory.Content.Length > budget)
                break;

            result.Add(memory);
            used += memory.Content.Length;
        }

        var wasTruncated = result.Count < memories.Count;
        return (result.ToArray(), wasTruncated);
    }
}
