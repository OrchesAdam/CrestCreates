using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.Authoring.Prompting;
using CrestCreates.Agent.Memory.Abstractions;
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
    private readonly LlmDescriptorAuthoringAgentOptions _options;
    private readonly TimeProvider _timeProvider;

    public LlmDescriptorAuthoringAgent(
        IDescriptorAuthoringPromptInputFactory promptInputFactory,
        IDescriptorAuthoringPromptBuilder promptBuilder,
        IDescriptorAuthoringModelClient modelClient,
        IDescriptorAuthoringOutputParser outputParser,
        IOptions<LlmDescriptorAuthoringAgentOptions> options,
        TimeProvider timeProvider)
    {
        _promptInputFactory = promptInputFactory;
        _promptBuilder = promptBuilder;
        _modelClient = modelClient;
        _outputParser = outputParser;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<DescriptorAuthoringResult> AuthorAsync(
        AgentAuthoringContext context,
        CancellationToken cancellationToken = default)
    {
        // 1. Create prompt input from context
        var promptInput = _promptInputFactory.Create(context);

        if (promptInput.PromptInputHash is null)
        {
            return new DescriptorAuthoringResult
            {
                Status = DescriptorAuthoringStatus.Failed,
                Plan = new DescriptorAuthoringPlan
                {
                    PlanId = "plan_error",
                    IntentText = context.Request.IntentText
                },
                DraftSet = new DescriptorDraftSet { DraftSetId = "draftset_error" },
                Diagnostics = new[]
                {
                    new DescriptorAuthoringDiagnostic
                    {
                        Code = DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput,
                        Message = "Prompt input hash computation failed.",
                        Severity = SeverityLevel.Error
                    }
                }
            };
        }

        // 2. Build prompt output
        var promptOutput = _promptBuilder.Build(promptInput);

        // 3. Send to model
        var modelRequest = new DescriptorAuthoringModelRequest
        {
            Prompt = promptOutput,
            ModelProfile = _options.ModelProfile
        };

        var modelResponse = await _modelClient.CompleteAsync(modelRequest, cancellationToken);

        // 4. Check for empty response (provider unavailable)
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

        // 5. Parse model response
        var parseContext = new DescriptorAuthoringParseContext
        {
            TenantId = context.Request.TenantId,
            AuthorId = _options.AuthorId,
            AuthorKind = DescriptorDraftAuthorKind.Agent,
            CreatedAt = _timeProvider.GetUtcNow(),
            IntentText = context.Request.IntentText,
            ExpectedPromptInputHash = promptInput.PromptInputHash.Value
        };

        return _outputParser.Parse(modelResponse.ResponseText, parseContext);
    }
}
