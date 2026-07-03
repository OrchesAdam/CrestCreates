using System.Text.Json;

namespace CrestCreates.Agent.Prompting.Abstractions;

public interface IAgentPromptCanonicalPayloadProjector<TPayload>
{
    void Write(Utf8JsonWriter writer, TPayload payload);
}

public interface IAgentPromptHashService
{
    CanonicalHash ComputeInputHash<TInput>(AgentPromptEvidenceCreationRequest<TInput> request);

    CanonicalHash? ComputeOutputHash<TOutput>(
        AgentPromptEvidenceCreationRequest<TOutput> request,
        CanonicalHash inputHash,
        AgentPromptProviderObservation? providerObservation,
        string? artifactKind = null,
        string? canonicalShapeVersion = null,
        string? purpose = null);
}

public interface IAgentPromptEvidenceFactory
{
    AgentPromptInputEvidence<TInput> CreateInputEvidence<TInput>(
        AgentPromptEvidenceCreationRequest<TInput> request);

    AgentPromptOutputEvidence<TOutput> CreateOutputEvidence<TOutput>(
        AgentPromptEvidenceCreationRequest<TOutput> request,
        CanonicalHash inputHash,
        AgentPromptProviderObservation? providerObservation = null,
        string? artifactKind = null,
        string? canonicalShapeVersion = null,
        string? purpose = null);
}

public interface IAgentPromptTemplateRegistry
{
    AgentPromptTemplateDescriptor? Find(AgentPromptTemplateId templateId, AgentPromptVersion version);
    IReadOnlyList<AgentPromptTemplateDescriptor> List();
}
