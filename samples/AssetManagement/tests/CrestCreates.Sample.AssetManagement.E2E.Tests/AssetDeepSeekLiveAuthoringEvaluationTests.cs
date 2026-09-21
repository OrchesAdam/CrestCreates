using System.Globalization;
using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring;
using CrestCreates.Agent.Authoring.Authoring;
using CrestCreates.Agent.Authoring.Http;
using CrestCreates.Agent.Authoring.Http.OpenAICompatible;
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Host;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// Opt-in provider evaluation. The test intentionally stops at authoring and
/// draft review so a provider response cannot approve, activate, or mutate the
/// Asset runtime.
/// </summary>
public sealed class AssetDeepSeekLiveAuthoringEvaluationTests
{
    private const string TenantId = "asset-live-eval-tenant";
    private const string AuthorId = "asset-live-eval-author";
    private const string RequestedModel = "deepseek-v4-flash";
    private const string MaintenanceDecisionSchema = "asset-management.schema.maintenance-decision";
    private const string Intent =
        "For this first bounded increment, create exactly one initial Asset maintenance HumanTask with id ht_asset_maintenance_initial_review. It must use the existing maintenance form and remain a separate first approval before the existing maintenance approval; do not create or update the workflow yet, and do not claim the complete two-stage workflow change.";

    [AssetLiveEvalFact]
    public async Task DeepSeek_AuthoringAndReview_ProducesAuditedAssetDraftEvidence()
    {
        var runId = Guid.NewGuid().ToString("N");
        var artifactPath = Environment.GetEnvironmentVariable("CREST_ASSET_LIVE_EVAL_ARTIFACT")
            ?? Path.Combine(Path.GetTempPath(), $"crest-asset-live-eval-{runId}.json");
        var baseline = AssetControlPlaneApprovalHarness.BuildDeployedBaseline();
        var artifact = new LiveEvaluationArtifact(runId, RequestedModel);
        var responseObservation = new AssetLiveResponseObservation();
        var semantic = new SemanticResult();
        var outputBudget = ResolveOutputBudget(Environment.GetEnvironmentVariable("CREST_ASSET_LIVE_EVAL_MAX_OUTPUT_TOKENS"));
        artifact.OutputBudget = new
        {
            configuredMaxOutputTokens = outputBudget.MaxOutputTokens,
            isValid = outputBudget.IsValid,
            diagnosticCode = outputBudget.DiagnosticCode
        };
        if (!outputBudget.IsValid)
        {
            semantic.Result = "InvalidOutputBudget";
            semantic.ExceptionStage = "budget";
            artifact.Semantic = semantic;
            artifact.HttpResponse = responseObservation.Snapshot();
            WriteArtifact(artifactPath, artifact);
            artifact.Semantic.Result.Should().Be("HumanTaskDraftReviewed", artifactPath);
            return;
        }

        var stage = "setup";
        try
        {
            stage = "composition";
            await using var harness = await AssetControlPlaneApprovalHarness.CreateAsync(
                TenantId,
                AuthorId,
                services => ConfigureLiveServices(services, AuthorId, responseObservation, outputBudget.MaxOutputTokens));
            var services = harness.Services;
            var topologyBuilder = services.GetRequiredService<CrestCreates.Metadata.Abstractions.IDescriptorTopologyBuilder>();
            stage = "context";
            var topology = topologyBuilder.Build(baseline);
            var focus = new DescriptorRef("workflow", AssetContractIds.MaintenanceWorkflow, 1);
            var contextPack = services.GetRequiredService<IMetadataContextPackBuilder>().Build(
                new MetadataContextPackRequest
                {
                    Scope = MetadataContextPackScope.DirectDependencies,
                    TenantId = TenantId,
                    Intent = Intent,
                    FocusDescriptors = [focus],
                    IncludeStableHashes = true
                },
                topology,
                baseline);

            artifact.Context = new
            {
                focusRef = FormatRef(focus),
                scope = MetadataContextPackScope.DirectDependencies.ToString(),
                descriptorCount = contextPack.Descriptors.Count,
                relationshipCount = contextPack.Relationships.Count,
                refs = contextPack.Descriptors.Select(d => FormatRef(d.Ref)).ToArray(),
                hashes = contextPack.Descriptors
                    .Select(d => new
                    {
                        @ref = FormatRef(d.Ref),
                        contractHash = d.Hashes?.ContractHash?.Value,
                        definitionHash = d.Hashes?.DefinitionHash?.Value
                    })
                    .ToArray(),
                diagnosticCodes = contextPack.Diagnostics.Select(d => d.Code.Value).ToArray()
            };

            stage = "memory";
            var memoryPack = await services.GetRequiredService<IAgentMemoryRetriever>()
                .RecallAsync(new AgentMemoryQuery
                {
                    TenantId = TenantId,
                    IntentText = Intent,
                    DescriptorRefs = [focus]
                });
            var authoringContext = await services.GetRequiredService<IAgentAuthoringContextBuilder>()
                .BuildAsync(
                    new AgentAuthoringRequest { TenantId = TenantId, IntentText = Intent },
                    contextPack,
                    memoryPack);

            stage = "authoring";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var result = await services.GetRequiredService<IDescriptorAuthoringAgent>()
                .AuthorAsync(authoringContext, timeout.Token);

            artifact.Authoring = new
            {
                status = result.Status.ToString(),
                diagnosticCodes = result.Diagnostics.Select(d => d.Code.Value).ToArray(),
                promptInputHash = result.PromptInputEvidence?.InputHash.Value,
                promptOutputHash = result.PromptOutputEvidence?.OutputHash?.Value,
                observedModel = result.PromptOutputEvidence?.ProviderObservation?.ModelName,
                observedProvider = result.PromptOutputEvidence?.ProviderObservation?.ProviderName,
                draftCount = result.DraftSet.Drafts.Count
            };

            semantic.AuthoringSucceeded = result.Status is DescriptorAuthoringStatus.Succeeded
                or DescriptorAuthoringStatus.SucceededWithDiagnostics;
            var currentInventory = baseline.ToList();
            var reviewService = services.GetRequiredService<IDescriptorDraftReviewService>();
            semantic.OnlySingleHumanTaskCreate = result.DraftSet.Drafts.Count == 1;
            var httpMetadata = responseObservation.Snapshot();
            artifact.HttpResponse = httpMetadata;
            if (semantic.AuthoringSucceeded)
            {
                for (var draftIndex = 0; draftIndex < result.DraftSet.Drafts.Count; draftIndex++)
                {
                    stage = "review";
                    var draft = result.DraftSet.Drafts[draftIndex];
                    var review = await reviewService.ReviewAsync(draft, currentInventory, timeout.Token);
                    var hasBlockingDiagnostics = review.Diagnostics.Any(d =>
                        d.Severity == SeverityLevel.Blocker || d.Severity == SeverityLevel.Error);
                    var governanceBlocked = review.GovernanceDecision?.MaxDecision
                        == CrestCreates.Metadata.Abstractions.DescriptorLifecycle.DescriptorLifecycleDecisionKind.Blocked;
                    var reviewPassed = review.ValidationResult.IsValid
                        && review.MaterializationResult?.IsMaterialized == true
                        && !hasBlockingDiagnostics
                        && !governanceBlocked;
                    artifact.Drafts.Add(new
                    {
                        index = draftIndex,
                        descriptorKind = draft.DescriptorKind.ToString(),
                        isExpectedHumanTaskRef = draft.DescriptorKind == DescriptorKind.HumanTask
                            && draft.DescriptorId == AssetContractIds.MaintenanceInitialHumanTask,
                        operation = draft.Operation.ToString(),
                        baseVersionMatchesExpected = draft.Operation == DescriptorDraftOperation.Create
                            && draft.BaseVersion is null,
                        proposedVersionMatchesExpected = draft.ProposedVersion == "1"
                    });
                    artifact.Reviews.Add(new
                    {
                        index = draftIndex,
                        validationStatus = review.ValidationResult.IsValid ? "Valid" : "Invalid",
                        materializationStatus = review.MaterializationResult?.IsMaterialized == true
                            ? "Materialized"
                            : review.MaterializationResult is null ? "NotRun" : "Failed",
                        blockingDiagnosticObserved = hasBlockingDiagnostics,
                        governanceBlocked,
                        reviewPassed,
                        diagnosticCodes = review.Diagnostics.Select(d => d.Code.Value).ToArray()
                    });

                    // A failed or blocked review never becomes the input to the next review.
                    if (reviewPassed)
                        currentInventory = review.MaterializationResult!.ProposedInventory.ToList();

                    if (draft.DescriptorKind == DescriptorKind.HumanTask
                        && draft.DescriptorId == AssetContractIds.MaintenanceInitialHumanTask
                        && draft.Operation == DescriptorDraftOperation.Create)
                    {
                        semantic.HumanTaskDraftObserved = true;
                        var candidate = draft.Payload.GetDescriptor() as HumanTaskDescriptor;
                        semantic.HumanTaskContractValid = candidate is not null
                            && candidate.Id == AssetContractIds.MaintenanceInitialHumanTask
                            && candidate.State == DescriptorState.Active
                            && candidate.Version == 1
                            && candidate.AssigneeStrategy == AssigneeStrategy.CandidateGroup
                            && candidate.Interaction.Id == AssetContractIds.MaintenanceForm
                            && candidate.Interaction.Version == 1
                            && HasExpectedFormSchema(baseline)
                            && candidate.InputSchema is null
                            && candidate.OutputSchema is null
                            && candidate.Outcomes.Select(o => o.Condition)
                                .OrderBy(value => value)
                                .SequenceEqual(new[] { CompletionCondition.Approve, CompletionCondition.Reject }.OrderBy(value => value));
                        semantic.HumanTaskReviewMaterialized = reviewPassed
                            && review.ProposedInventory?.Any(d =>
                                d.Kind == DescriptorKind.HumanTask
                                && d.Id == AssetContractIds.MaintenanceInitialHumanTask) == true;
                    }
                }
            }
            else
            {
                semantic.Result = IsOutputBudgetExhausted(httpMetadata)
                    ? "OutputBudgetExhausted"
                    : "AuthoringFailure";
            }

            if (semantic.AuthoringSucceeded)
            {
                semantic.Result = semantic.HumanTaskDraftObserved
                    && semantic.OnlySingleHumanTaskCreate
                    && semantic.HumanTaskContractValid
                    && semantic.HumanTaskReviewMaterialized
                    ? "HumanTaskDraftReviewed"
                    : "SemanticFailure";
            }
            artifact.Semantic = semantic;
            artifact.HttpResponse = responseObservation.Snapshot();
        }
        catch (Exception exception)
        {
            semantic.Result = "UnhandledEvaluationFailure";
            semantic.ExceptionStage = stage;
            semantic.ExceptionType = exception.GetType().FullName;
            artifact.Semantic = semantic;
            artifact.HttpResponse = responseObservation.Snapshot();
        }
        finally
        {
            WriteArtifact(artifactPath, artifact);
        }

        artifact.Semantic.Result.Should().Be("HumanTaskDraftReviewed", artifactPath);
    }

    private static bool HasExpectedFormSchema(IReadOnlyList<IDescriptor> baseline)
        => baseline.OfType<FormDescriptor>().Any(form =>
            form.Id == AssetContractIds.MaintenanceForm
            && form.Version == 1
            && form.Schema.Id == MaintenanceDecisionSchema
            && form.Schema.Version == 1);

    private static string FormatRef(DescriptorRef reference)
        => $"{reference.Namespace}/{reference.Id} v{reference.Version?.ToString() ?? "latest"}";

    internal static AssetLiveOutputBudgetResolution ResolveOutputBudget(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return new(true, 4096, "DEFAULT_OUTPUT_BUDGET");

        return int.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value is >= 1 and <= 16384
            ? new(true, value, "OUTPUT_BUDGET_ACCEPTED")
            : new(false, 0, "OUTPUT_BUDGET_INVALID");
    }

    internal static bool IsOutputBudgetExhausted(AssetLiveResponseMetadata metadata)
        => metadata.HttpStatus == 200
            && metadata.FinishReason == "length"
            && metadata.ContentCharacterCount == 0;

    private static void WriteArtifact(string path, LiveEvaluationArtifact artifact)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void ConfigureLiveServices(
        IServiceCollection services,
        string authorId,
        AssetLiveResponseObservation responseObservation,
        int maxOutputTokens = 4096)
    {
        services.AddSingleton(responseObservation);
        services.AddTransient<AssetLiveResponseObservationHandler>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build());
        services.AddAgentPrompting();
        services.AddAgentMemoryReadRuntime();
        services.AddDescriptorAuthoring();
        services.AddOpenAICompatibleAuthoringProvider(
            providerName: "deepseek",
            credentialReference: "DEEPSEEK_API_KEY",
            endpoint: new Uri("https://api.deepseek.com/v1/chat/completions"));
        services.AddHttpClient<IDescriptorAuthoringModelClient, OpenAICompatibleDescriptorAuthoringModelClient>()
            .AddHttpMessageHandler<AssetLiveResponseObservationHandler>();
        services.Configure<LlmDescriptorAuthoringAgentOptions>(options =>
        {
            options.AuthorId = authorId;
            options.ProviderProfileRef = new AgentPromptProviderProfileRef("deepseek");
            options.ModelProfile = new DescriptorAuthoringModelProfile
            {
                ProfileName = "deepseek-v4-flash-live-eval",
                ProviderName = "deepseek",
                ModelName = RequestedModel,
                SupportsJsonMode = true,
                MaxOutputTokens = maxOutputTokens
            };
        });
        services.AddSingleton<IDescriptorAuthoringAgent, LlmDescriptorAuthoringAgent>();
    }

    private sealed class LiveEvaluationArtifact(string runId, string requestedModel)
    {
        public string RunId { get; } = runId;
        public string RequestedModel { get; } = requestedModel;
        public object? Context { get; set; }
        public object? Authoring { get; set; }
        public object? OutputBudget { get; set; }
        public AssetLiveResponseMetadata HttpResponse { get; set; } = AssetLiveResponseMetadata.Empty;
        public List<object> Drafts { get; } = [];
        public List<object> Reviews { get; } = [];
        public SemanticResult Semantic { get; set; } = new();
    }

    private sealed class SemanticResult
    {
        public string Result { get; set; } = "NotEvaluated";
        public bool OnlySingleHumanTaskCreate { get; set; }
        public bool AuthoringSucceeded { get; set; }
        public bool HumanTaskDraftObserved { get; set; }
        public bool HumanTaskContractValid { get; set; }
        public bool HumanTaskReviewMaterialized { get; set; }
        public string? ExceptionStage { get; set; }
        public string? ExceptionType { get; set; }
    }
}

internal readonly record struct AssetLiveOutputBudgetResolution(
    bool IsValid,
    int MaxOutputTokens,
    string DiagnosticCode);

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AssetLiveEvalFactAttribute : FactAttribute
{
    public AssetLiveEvalFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CREST_ASSET_LIVE_EVAL"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set CREST_ASSET_LIVE_EVAL=1 to run the opt-in live model evaluation.";
        }
    }
}
