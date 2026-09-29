using System.Collections.Concurrent;
using System.Text.Json;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>Explicit development store using the same immutable JSON contract as durable stores.</summary>
public sealed class InMemoryAgentReviewArtifactStore : IAgentReviewArtifactStore
{
    private readonly ConcurrentDictionary<(string TenantId, string ReviewResultId), string> _records = new();

    public Task InsertAsync(AgentReviewArtifactEnvelope artifact, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifact);
        artifact.Validate();
        var key = (artifact.TenantId, artifact.ReviewResultId);
        var json = JsonSerializer.Serialize(artifact, AgentReviewArtifactEnvelopeJsonSerializerContext.Default.AgentReviewArtifactEnvelope);
        if (!_records.TryAdd(key, json))
            throw new InvalidOperationException($"Review artifact '{artifact.ReviewResultId}' already exists for tenant '{artifact.TenantId}'.");
        return Task.CompletedTask;
    }

    public Task<AgentReviewArtifactEnvelope?> GetAsync(string tenantId, string reviewResultId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_records.TryGetValue((tenantId, reviewResultId), out var json))
            return Task.FromResult<AgentReviewArtifactEnvelope?>(null);
        return Task.FromResult<AgentReviewArtifactEnvelope?>(Decode(json, tenantId, reviewResultId));
    }

    public Task<IReadOnlyList<AgentReviewArtifactEnvelope>> ListAsync(string tenantId, string? draftId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var records = _records
            .Where(pair => StringComparer.Ordinal.Equals(pair.Key.TenantId, tenantId))
            .Select(pair => Decode(pair.Value, pair.Key.TenantId, pair.Key.ReviewResultId))
            .Where(record => draftId is null || StringComparer.Ordinal.Equals(record.DraftId, draftId))
            .OrderBy(record => record.DraftId, StringComparer.Ordinal)
            .ThenBy(record => record.CreatedAt)
            .ThenBy(record => record.ReviewResultId, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult<IReadOnlyList<AgentReviewArtifactEnvelope>>(Array.AsReadOnly(records));
    }

    public async Task<AgentReviewArtifactEnvelope?> GetLatestAsync(string tenantId, string draftId, CancellationToken ct = default)
    {
        var records = await ListAsync(tenantId, draftId, ct);
        return records.OrderByDescending(record => record.CreatedAt)
            .ThenByDescending(record => record.ReviewResultId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static AgentReviewArtifactEnvelope Decode(string json, string tenantId, string reviewResultId)
    {
        var artifact = JsonSerializer.Deserialize(json, AgentReviewArtifactEnvelopeJsonSerializerContext.Default.AgentReviewArtifactEnvelope)
            ?? throw new InvalidOperationException("Stored review artifact payload decoded to null.");
        artifact.Validate();
        if (!StringComparer.Ordinal.Equals(artifact.TenantId, tenantId) ||
            !StringComparer.Ordinal.Equals(artifact.ReviewResultId, reviewResultId))
            throw new InvalidOperationException("Stored review artifact identity does not match its key.");
        return artifact;
    }
}
