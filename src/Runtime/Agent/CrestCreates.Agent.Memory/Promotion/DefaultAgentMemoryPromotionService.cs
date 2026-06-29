using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Promotion;

public sealed class DefaultAgentMemoryPromotionService : IAgentMemoryPromotionService
{
    private readonly IAgentMemoryStore _store;

    public DefaultAgentMemoryPromotionService(IAgentMemoryStore store)
    {
        _store = store;
    }

    public async ValueTask<AgentMemoryItem> PromoteAsync(string tenantId, string candidateId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
    {
        ValidateOperationRequest(tenantId, request, nameof(PromoteAsync));

        var candidate = await _store.GetCandidateAsync(tenantId, candidateId, cancellationToken);
        if (candidate is null)
        {
            throw new InvalidOperationException($"Candidate '{candidateId}' not found for tenant '{tenantId}'.");
        }

        if (candidate.Status != AgentMemoryStatus.Candidate)
        {
            throw new InvalidOperationException($"Candidate '{candidateId}' has status '{candidate.Status}', expected 'Candidate'.");
        }

        var memory = new AgentMemoryItem
        {
            MemoryId = candidate.CandidateId,
            TenantId = candidate.TenantId,
            Kind = candidate.Kind,
            Content = candidate.Content,
            CanonicalContentHash = candidate.CanonicalContentHash,
            PromotedAt = request.Timestamp,
            Confidence = candidate.Confidence,
            Status = AgentMemoryStatus.Active,
            IsAuthoritative = false,
            Tags = candidate.Tags,
            DescriptorRefs = candidate.DescriptorRefs,
            SourceRefs = candidate.SourceRefs
        };

        await _store.SaveMemoryAsync(memory, cancellationToken);

        var updatedCandidate = candidate with { Status = AgentMemoryStatus.Active };
        await _store.SaveCandidateAsync(updatedCandidate, cancellationToken);

        return memory;
    }

    public async ValueTask RejectAsync(string tenantId, string candidateId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
    {
        ValidateOperationRequest(tenantId, request, nameof(RejectAsync));

        var candidate = await _store.GetCandidateAsync(tenantId, candidateId, cancellationToken);
        if (candidate is null)
        {
            throw new InvalidOperationException($"Candidate '{candidateId}' not found for tenant '{tenantId}'.");
        }

        if (candidate.Status != AgentMemoryStatus.Candidate)
        {
            throw new InvalidOperationException($"Candidate '{candidateId}' has status '{candidate.Status}', only 'Candidate' can be rejected.");
        }

        var rejected = candidate with { Status = AgentMemoryStatus.Rejected };
        await _store.SaveCandidateAsync(rejected, cancellationToken);
    }

    public async ValueTask<AgentMemoryItem> SupersedeAsync(string tenantId, string memoryId, AgentMemoryCandidate replacement, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
    {
        ValidateOperationRequest(tenantId, request, nameof(SupersedeAsync));

        var existing = await _store.GetMemoryAsync(tenantId, memoryId, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException($"Memory '{memoryId}' not found for tenant '{tenantId}'.");
        }

        if (existing.Status != AgentMemoryStatus.Active)
        {
            throw new InvalidOperationException($"Memory '{memoryId}' has status '{existing.Status}', only 'Active' memories can be superseded.");
        }

        var superseded = existing with
        {
            Status = AgentMemoryStatus.Superseded,
            SupersededByMemoryId = replacement.CandidateId
        };
        await _store.SaveMemoryAsync(superseded, cancellationToken);

        var newMemory = new AgentMemoryItem
        {
            MemoryId = replacement.CandidateId,
            TenantId = replacement.TenantId,
            Kind = replacement.Kind,
            Content = replacement.Content,
            CanonicalContentHash = replacement.CanonicalContentHash,
            PromotedAt = request.Timestamp,
            Confidence = replacement.Confidence,
            Status = AgentMemoryStatus.Active,
            IsAuthoritative = false,
            Tags = replacement.Tags,
            DescriptorRefs = replacement.DescriptorRefs,
            SourceRefs = replacement.SourceRefs,
            SupersedesMemoryId = memoryId
        };
        await _store.SaveMemoryAsync(newMemory, cancellationToken);

        return newMemory;
    }

    public async ValueTask ArchiveAsync(string tenantId, string memoryId, AgentMemoryOperationRequest request, CancellationToken cancellationToken = default)
    {
        ValidateOperationRequest(tenantId, request, nameof(ArchiveAsync));

        var memory = await _store.GetMemoryAsync(tenantId, memoryId, cancellationToken);
        if (memory is null)
        {
            throw new InvalidOperationException($"Memory '{memoryId}' not found for tenant '{tenantId}'.");
        }

        if (memory.Status is not (AgentMemoryStatus.Active or AgentMemoryStatus.Superseded))
        {
            throw new InvalidOperationException($"Memory '{memoryId}' has status '{memory.Status}', only 'Active' or 'Superseded' memories can be archived.");
        }

        var archived = memory with { Status = AgentMemoryStatus.Archived };
        await _store.SaveMemoryAsync(archived, cancellationToken);
    }

    private static void ValidateOperationRequest(string tenantId, AgentMemoryOperationRequest request, string operationName)
    {
        // 1. Tenant match
        if (request.TenantId != tenantId)
        {
            throw new InvalidOperationException(
                $"Operation '{operationName}' failed: {AgentMemoryDiagnosticCodes.InvalidOperationTenantMismatch} - " +
                $"Tenant mismatch. Expected '{tenantId}', got '{request.TenantId}'.");
        }

        // 2. Actor
        if (request.Actor is null)
        {
            throw new InvalidOperationException(
                $"Operation '{operationName}' failed: {AgentMemoryDiagnosticCodes.InvalidOperationMissingActor} - " +
                "Actor is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Actor.ActorId))
        {
            throw new InvalidOperationException(
                $"Operation '{operationName}' failed: {AgentMemoryDiagnosticCodes.InvalidOperationMissingActor} - " +
                "Actor.ActorId is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Actor.ActorKind))
        {
            throw new InvalidOperationException(
                $"Operation '{operationName}' failed: {AgentMemoryDiagnosticCodes.InvalidOperationMissingActor} - " +
                "Actor.ActorKind is required.");
        }

        // 3. Reason
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new InvalidOperationException(
                $"Operation '{operationName}' failed: {AgentMemoryDiagnosticCodes.InvalidOperationMissingReason} - " +
                "Reason is required.");
        }

        // 4. Timestamp
        if (request.Timestamp == default(DateTimeOffset))
        {
            throw new InvalidOperationException(
                $"Operation '{operationName}' failed: {AgentMemoryDiagnosticCodes.InvalidOperationMissingTimestamp} - " +
                "Timestamp is required and must not be default.");
        }

        // 5. SourceRefs or Explanation
        if (request.SourceRefs.Count == 0 && string.IsNullOrWhiteSpace(request.Explanation))
        {
            throw new InvalidOperationException(
                $"Operation '{operationName}' failed: {AgentMemoryDiagnosticCodes.InvalidOperationMissingSourceOrExplanation} - " +
                "SourceRefs or Explanation is required.");
        }
    }
}
