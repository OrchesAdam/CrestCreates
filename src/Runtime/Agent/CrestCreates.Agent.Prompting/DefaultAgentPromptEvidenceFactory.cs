using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Prompting;

public sealed class DefaultAgentPromptEvidenceFactory : IAgentPromptEvidenceFactory
{
    private readonly IAgentPromptHashService _hashService;
    private readonly TimeProvider _timeProvider;

    public DefaultAgentPromptEvidenceFactory(
        IAgentPromptHashService hashService,
        TimeProvider timeProvider)
    {
        _hashService = hashService;
        _timeProvider = timeProvider;
    }

    public AgentPromptInputEvidence<TInput> CreateInputEvidence<TInput>(
        AgentPromptEvidenceCreationRequest<TInput> request)
    {
        var inputHash = _hashService.ComputeInputHash(request);
        var now = _timeProvider.GetUtcNow();

        return new AgentPromptInputEvidence<TInput>
        {
            TemplateId = request.TemplateId,
            TemplateVersion = request.TemplateVersion,
            Purpose = request.Purpose,
            ContractVersion = request.ContractVersion,
            ModelProfileRef = request.ModelProfileRef,
            ProviderProfileRef = request.ProviderProfileRef,
            Input = request.Payload,
            InputHash = inputHash,
            CreatedAt = now,
            TenantId = request.TenantId,
            ActorId = request.ActorId,
            CorrelationId = request.CorrelationId
        };
    }

    public AgentPromptOutputEvidence<TOutput> CreateOutputEvidence<TOutput>(
        AgentPromptEvidenceCreationRequest<TOutput> request,
        CanonicalHash inputHash,
        AgentPromptProviderObservation? providerObservation = null,
        string? artifactKind = null,
        string? canonicalShapeVersion = null,
        string? purpose = null)
    {
        var outputHash = _hashService.ComputeOutputHash(request, inputHash, providerObservation, artifactKind, canonicalShapeVersion, purpose);
        var now = _timeProvider.GetUtcNow();

        var diagnostics = new List<AgentPromptDiagnostic>();
        if (outputHash is null)
        {
            diagnostics.Add(new AgentPromptDiagnostic
            {
                Code = AgentPromptDiagnosticCodes.OutputHashUnavailable,
                Severity = "Warning",
                Message = "Output evidence hash is unavailable because no canonical payload projector was registered for the output type."
            });
        }

        return new AgentPromptOutputEvidence<TOutput>
        {
            TemplateId = request.TemplateId,
            TemplateVersion = request.TemplateVersion,
            Purpose = request.Purpose,
            ContractVersion = request.ContractVersion,
            ModelProfileRef = request.ModelProfileRef,
            ProviderProfileRef = request.ProviderProfileRef,
            InputHash = inputHash,
            OutputHash = outputHash,
            Output = request.Payload,
            ProviderObservation = providerObservation,
            CreatedAt = now,
            Diagnostics = diagnostics
        };
    }
}
