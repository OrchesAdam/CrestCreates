using System.Buffers;
using System.Text.Json;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Agent.Prompting;

public sealed class DefaultAgentPromptHashService : IAgentPromptHashService
{
    private readonly ICanonicalHashComputer _hashComputer;
    private readonly IServiceProvider _serviceProvider;

    public DefaultAgentPromptHashService(
        ICanonicalHashComputer hashComputer,
        IServiceProvider serviceProvider)
    {
        _hashComputer = hashComputer;
        _serviceProvider = serviceProvider;
    }

    public CanonicalHash ComputeInputHash<TInput>(AgentPromptEvidenceCreationRequest<TInput> request)
    {
        var projector = _serviceProvider.GetService<IAgentPromptCanonicalPayloadProjector<TInput>>();
        if (projector is null)
        {
            throw new InvalidOperationException(
                $"No IAgentPromptCanonicalPayloadProjector<TInput> registered for type {typeof(TInput).Name}. " +
                "Prompt hash projection requires an AoT-safe canonical payload projector. " +
                "Do not use reflection-based JSON serialization.");
        }

        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = CanonicalHashArtifactNames.AgentPromptInputEvidence,
                Purpose = CanonicalHashPurposeNames.SourceIdentity,
                Scope = CanonicalHashScopeNames.InternalFull,
                AlgorithmVersion = DefaultCanonicalHashComputer.AlgorithmVersion,
                ContractVersion = CanonicalHashContractVersions.DescriptorHash,
                CanonicalShapeVersion = "agent-prompt-input-evidence-shape-v1"
            },
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("templateId", request.TemplateId.Value);
                writer.WriteString("templateVersion", request.TemplateVersion.Value);
                writer.WriteString("purpose", request.Purpose.ToString());
                writer.WriteString("contractVersion", request.ContractVersion.Value);
                writer.WriteString("modelProfileRef", request.ModelProfileRef.Value);
                writer.WriteString("providerProfileRef", request.ProviderProfileRef.Value);
                writer.WritePropertyName("payload");
                projector.Write(writer, request.Payload);
                writer.WriteEndObject();
            });

        return _hashComputer.ComputeFromProjection(projection);
    }

    public CanonicalHash? ComputeOutputHash<TOutput>(
        AgentPromptEvidenceCreationRequest<TOutput> request,
        CanonicalHash inputHash,
        AgentPromptProviderObservation? providerObservation)
    {
        var projector = _serviceProvider.GetService<IAgentPromptCanonicalPayloadProjector<TOutput>>();
        if (projector is null)
        {
            return null;
        }

        var projection = CanonicalHashProjectionResult.Create(
            new CanonicalHashMetadata
            {
                ArtifactKind = CanonicalHashArtifactNames.AgentPromptOutputEvidence,
                Purpose = CanonicalHashPurposeNames.AuditEvidence,
                Scope = CanonicalHashScopeNames.InternalFull,
                AlgorithmVersion = DefaultCanonicalHashComputer.AlgorithmVersion,
                ContractVersion = CanonicalHashContractVersions.DescriptorHash,
                CanonicalShapeVersion = "agent-prompt-output-evidence-shape-v1"
            },
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("templateId", request.TemplateId.Value);
                writer.WriteString("templateVersion", request.TemplateVersion.Value);
                writer.WriteString("purpose", request.Purpose.ToString());
                writer.WriteString("contractVersion", request.ContractVersion.Value);
                writer.WriteString("modelProfileRef", request.ModelProfileRef.Value);
                writer.WriteString("providerProfileRef", request.ProviderProfileRef.Value);
                writer.WriteString("inputHash", inputHash.Value);
                if (providerObservation is not null)
                {
                    if (providerObservation.ProviderName is not null)
                        writer.WriteString("providerName", providerObservation.ProviderName);
                    if (providerObservation.ModelName is not null)
                        writer.WriteString("modelName", providerObservation.ModelName);
                }
                writer.WritePropertyName("payload");
                projector.Write(writer, request.Payload);
                writer.WriteEndObject();
            });

        return _hashComputer.ComputeFromProjection(projection);
    }
}
