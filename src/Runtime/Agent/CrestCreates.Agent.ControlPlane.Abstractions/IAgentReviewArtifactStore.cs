namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>Single authority for immutable review artifacts.</summary>
public interface IAgentReviewArtifactStore
{
    Task InsertAsync(AgentReviewArtifactEnvelope artifact, CancellationToken ct = default);

    Task<AgentReviewArtifactEnvelope?> GetAsync(
        string tenantId, string reviewResultId, CancellationToken ct = default);

    Task<IReadOnlyList<AgentReviewArtifactEnvelope>> ListAsync(
        string tenantId, string? draftId = null, CancellationToken ct = default);

    Task<AgentReviewArtifactEnvelope?> GetLatestAsync(
        string tenantId, string draftId, CancellationToken ct = default);
}
