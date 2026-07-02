using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestCreates.Agent.Authoring.Authoring;

public sealed class LlmDescriptorAuthoringAgent : IDescriptorAuthoringAgent
{
    private readonly IDescriptorAuthoringPromptInputFactory _promptInputFactory;
    private readonly IDescriptorAuthoringPromptBuilder _promptBuilder;
    private readonly IDescriptorAuthoringModelClient _modelClient;
    private readonly IDescriptorAuthoringOutputParser _outputParser;
    private readonly IAgentPromptEvidenceFactory _promptEvidenceFactory;
    private readonly LlmDescriptorAuthoringAgentOptions _options;
    private readonly TimeProvider _timeProvider;

    public LlmDescriptorAuthoringAgent(
        IDescriptorAuthoringPromptInputFactory promptInputFactory,
        IDescriptorAuthoringPromptBuilder promptBuilder,
        IDescriptorAuthoringModelClient modelClient,
        IDescriptorAuthoringOutputParser outputParser,
        IAgentPromptEvidenceFactory promptEvidenceFactory,
        IOptions<LlmDescriptorAuthoringAgentOptions> options,
        TimeProvider timeProvider)
    {
        _promptInputFactory = promptInputFactory;
        _promptBuilder = promptBuilder;
        _modelClient = modelClient;
        _outputParser = outputParser;
        _promptEvidenceFactory = promptEvidenceFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<DescriptorAuthoringResult> AuthorAsync(
        AgentAuthoringContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Create raw prompt input from context (without hash)
        var rawPromptInput = _promptInputFactory.Create(context);

        // 2. Create input evidence and set the hash
        var inputEvidenceRequest = new AgentPromptEvidenceCreationRequest<DescriptorAuthoringPromptInput>
        {
            TemplateId = _options.PromptTemplateId,
            TemplateVersion = _options.PromptTemplateVersion,
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = _options.PromptContractVersion,
            ModelProfileRef = new AgentPromptModelProfileRef(_options.ModelProfile.ProfileName),
            ProviderProfileRef = _options.ProviderProfileRef,
            Payload = rawPromptInput,
            TenantId = context.Request.TenantId
        };

        var inputEvidence = _promptEvidenceFactory.CreateInputEvidence(inputEvidenceRequest);
        var promptInput = rawPromptInput with { PromptInputHash = inputEvidence.InputHash };

        // 3. Build prompt output
        var promptOutput = _promptBuilder.Build(promptInput);

        // 4. Send to model
        var modelRequest = new DescriptorAuthoringModelRequest
        {
            Prompt = promptOutput,
            ModelProfile = _options.ModelProfile
        };

        var modelResponse = await _modelClient.CompleteAsync(modelRequest, cancellationToken);

        // 5. Create provider observation from model response
        var providerObservation = new AgentPromptProviderObservation
        {
            ProviderName = modelResponse.ProviderName,
            ModelName = modelResponse.ModelName
        };

        // 6. Create output evidence projection (excluding ResponseText)
        var outputProjection = new DescriptorAuthoringModelResponseEvidenceProjection
        {
            ProviderName = modelResponse.ProviderName,
            ModelName = modelResponse.ModelName,
            PromptInputHash = modelResponse.PromptInputHash,
            FailureKind = modelResponse.FailureKind,
            FailureDetail = modelResponse.FailureDetail
        };

        // 7. Create output evidence
        var outputEvidenceRequest = new AgentPromptEvidenceCreationRequest<DescriptorAuthoringModelResponseEvidenceProjection>
        {
            TemplateId = _options.PromptTemplateId,
            TemplateVersion = _options.PromptTemplateVersion,
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = _options.PromptContractVersion,
            ModelProfileRef = new AgentPromptModelProfileRef(_options.ModelProfile.ProfileName),
            ProviderProfileRef = _options.ProviderProfileRef,
            Payload = outputProjection,
            TenantId = context.Request.TenantId
        };

        var outputEvidence = _promptEvidenceFactory.CreateOutputEvidence(
            outputEvidenceRequest, inputEvidence.InputHash, providerObservation);

        var inputSummary = AgentPromptEvidenceSummaryFactory.CreateInputSummary(inputEvidence);
        var outputSummary = AgentPromptEvidenceSummaryFactory.CreateOutputSummary(outputEvidence);

        // 8. Check for empty response (provider unavailable)
        if (string.IsNullOrWhiteSpace(modelResponse.ResponseText))
        {
            var (status, code, message) = modelResponse.FailureKind switch
            {
                DescriptorAuthoringProviderFailureKind.CredentialUnavailable
                    => (DescriptorAuthoringStatus.ProviderUnavailable,
                        DescriptorAuthoringDiagnosticCodes.CredentialUnavailable,
                        $"Credential unavailable: {modelResponse.FailureDetail ?? "no detail"}"),
                DescriptorAuthoringProviderFailureKind.CredentialRejected
                    => (DescriptorAuthoringStatus.ProviderUnavailable,
                        DescriptorAuthoringDiagnosticCodes.CredentialRejected,
                        $"Credential rejected: {modelResponse.FailureDetail ?? "no detail"}"),
                DescriptorAuthoringProviderFailureKind.Unauthorized
                    => (DescriptorAuthoringStatus.ProviderUnavailable,
                        DescriptorAuthoringDiagnosticCodes.ProviderUnauthorized,
                        $"Provider unauthorized: {modelResponse.FailureDetail ?? "no detail"}"),
                DescriptorAuthoringProviderFailureKind.RateLimited
                    => (DescriptorAuthoringStatus.ProviderUnavailable,
                        DescriptorAuthoringDiagnosticCodes.ProviderRateLimited,
                        $"Provider rate limited: {modelResponse.FailureDetail ?? "no detail"}"),
                DescriptorAuthoringProviderFailureKind.Timeout
                    => (DescriptorAuthoringStatus.ProviderUnavailable,
                        DescriptorAuthoringDiagnosticCodes.ProviderTimeout,
                        $"Provider timeout: {modelResponse.FailureDetail ?? "no detail"}"),
                DescriptorAuthoringProviderFailureKind.NetworkError
                    => (DescriptorAuthoringStatus.ProviderUnavailable,
                        DescriptorAuthoringDiagnosticCodes.ProviderUnavailable,
                        $"Provider network error: {modelResponse.FailureDetail ?? "no detail"}"),
                DescriptorAuthoringProviderFailureKind.Unknown
                    => (DescriptorAuthoringStatus.ProviderUnavailable,
                        DescriptorAuthoringDiagnosticCodes.ProviderUnavailable,
                        $"Provider unavailable (unknown): {modelResponse.FailureDetail ?? "no detail"}"),
                _ => (DescriptorAuthoringStatus.ProviderUnavailable,
                      DescriptorAuthoringDiagnosticCodes.ProviderUnavailable,
                      $"Provider unavailable: {modelResponse.FailureDetail ?? "no detail"}")
            };

            return new DescriptorAuthoringResult
            {
                Status = status,
                Plan = new DescriptorAuthoringPlan
                {
                    PlanId = "plan_provider_failure",
                    IntentText = context.Request.IntentText
                },
                DraftSet = new DescriptorDraftSet { DraftSetId = "draftset_provider_failure" },
                PromptInputEvidence = inputSummary,
                PromptOutputEvidence = outputSummary,
                Diagnostics = new[]
                {
                    new DescriptorAuthoringDiagnostic
                    {
                        Code = code,
                        Message = message,
                        Severity = SeverityLevel.Error
                    }
                }
            };
        }

        // 9. Parse model response
        var parseContext = new DescriptorAuthoringParseContext
        {
            TenantId = context.Request.TenantId,
            AuthorId = _options.AuthorId,
            AuthorKind = DescriptorDraftAuthorKind.Agent,
            CreatedAt = _timeProvider.GetUtcNow(),
            IntentText = context.Request.IntentText,
            ExpectedPromptInputHash = promptInput.PromptInputHash.Value
        };

        var result = _outputParser.Parse(modelResponse.ResponseText, parseContext);
        return result with
        {
            PromptInputEvidence = inputSummary,
            PromptOutputEvidence = outputSummary
        };
    }
}
