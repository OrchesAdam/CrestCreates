using System.Collections.Concurrent;
using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Stores;

public sealed class InMemoryAgentMemoryStore : IAgentMemoryStore
{
    private readonly ConcurrentDictionary<(string TenantId, string CandidateId), AgentMemoryCandidate> _candidates = new();
    private readonly ConcurrentDictionary<(string TenantId, string MemoryId), AgentMemoryItem> _memories = new();

    public ValueTask SaveCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default)
    {
        _candidates[(candidate.TenantId, candidate.CandidateId)] = candidate with
        {
            Tags = candidate.Tags.ToArray(),
            DescriptorRefs = candidate.DescriptorRefs.ToArray(),
            SourceRefs = candidate.SourceRefs.ToArray(),
            RedactionKinds = candidate.RedactionKinds.ToArray(),
            SanitizationDiagnostics = candidate.SanitizationDiagnostics.ToArray()
        };
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentMemoryCandidate?> GetCandidateAsync(string tenantId, string candidateId, CancellationToken cancellationToken = default)
    {
        _candidates.TryGetValue((tenantId, candidateId), out var candidate);
        if (candidate is null) return new ValueTask<AgentMemoryCandidate?>((AgentMemoryCandidate?)null);

        var snapshot = candidate with
        {
            Tags = candidate.Tags.ToArray(),
            DescriptorRefs = candidate.DescriptorRefs.ToArray(),
            SourceRefs = candidate.SourceRefs.ToArray(),
            RedactionKinds = candidate.RedactionKinds.ToArray(),
            SanitizationDiagnostics = candidate.SanitizationDiagnostics.ToArray()
        };
        return new ValueTask<AgentMemoryCandidate?>(snapshot);
    }

    public ValueTask SaveMemoryAsync(AgentMemoryItem memory, CancellationToken cancellationToken = default)
    {
        _memories[(memory.TenantId, memory.MemoryId)] = memory with
        {
            Tags = memory.Tags.ToArray(),
            DescriptorRefs = memory.DescriptorRefs.ToArray(),
            SourceRefs = memory.SourceRefs.ToArray(),
            RedactionKinds = memory.RedactionKinds.ToArray(),
            SanitizationDiagnostics = memory.SanitizationDiagnostics.ToArray()
        };
        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentMemoryItem?> GetMemoryAsync(string tenantId, string memoryId, CancellationToken cancellationToken = default)
    {
        _memories.TryGetValue((tenantId, memoryId), out var memory);
        if (memory is null) return new ValueTask<AgentMemoryItem?>((AgentMemoryItem?)null);

        var snapshot = memory with
        {
            Tags = memory.Tags.ToArray(),
            DescriptorRefs = memory.DescriptorRefs.ToArray(),
            SourceRefs = memory.SourceRefs.ToArray(),
            RedactionKinds = memory.RedactionKinds.ToArray(),
            SanitizationDiagnostics = memory.SanitizationDiagnostics.ToArray()
        };
        return new ValueTask<AgentMemoryItem?>(snapshot);
    }

    public ValueTask<IReadOnlyList<AgentMemoryItem>> ListMemoriesAsync(AgentMemoryQuery query, CancellationToken cancellationToken = default)
    {
        var results = _memories.Values
            .Where(m => m.TenantId == query.TenantId)
            .Where(m => query.Kinds.Count == 0 || query.Kinds.Contains(m.Kind))
            .Where(m => query.Tags.Count == 0 || query.Tags.Any(t => m.Tags.Contains(t)))
            .Where(m => query.MemoryIds.Count == 0 || query.MemoryIds.Contains(m.MemoryId))
            .Where(m => FilterByDescriptorRefs(m, query))
            .Where(m => FilterByStatus(m, query))
            .OrderBy(m => m.MemoryId, StringComparer.Ordinal)
            .Select(m => m with
            {
                Tags = m.Tags.ToArray(),
                DescriptorRefs = m.DescriptorRefs.ToArray(),
                SourceRefs = m.SourceRefs.ToArray(),
                RedactionKinds = m.RedactionKinds.ToArray(),
                SanitizationDiagnostics = m.SanitizationDiagnostics.ToArray()
            })
            .ToArray();

        return new ValueTask<IReadOnlyList<AgentMemoryItem>>(results);
    }

    private static bool FilterByStatus(AgentMemoryItem memory, AgentMemoryQuery query)
    {
        return memory.Status switch
        {
            AgentMemoryStatus.Active => true,
            AgentMemoryStatus.Superseded => query.IncludeSuperseded,
            AgentMemoryStatus.Archived => query.IncludeArchived,
            AgentMemoryStatus.Candidate => false,
            _ => false
        };
    }

    private static bool FilterByDescriptorRefs(AgentMemoryItem memory, AgentMemoryQuery query)
    {
        if (query.DescriptorRefs.Count == 0) return true;
        return query.DescriptorRefs.Any(qr =>
            memory.DescriptorRefs.Any(mr => mr.Equals(qr)));
    }
}
