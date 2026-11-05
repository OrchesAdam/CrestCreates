using System.Text.Json;
using System.Text.Json.Nodes;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Sample.AssetManagement.Contracts;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// Implementation audit S2-R1-01, at the level the verdict is actually made: the offline B12 entry, driven
/// through the real agent, parser and review composition by a recorded provider. The finding was that the
/// historical route — a drafts candidate the framework's own allowlist blocks — passed the coverage assertion
/// with zero explicit entries. Zero side effects are necessary and are still asserted here, but only an
/// explicit `8a.v1` terminal outcome with real entry content counts as coverage (§10.4/§10.7).
/// </summary>
public sealed class AssetB12CoverageEntryTests
{
    private const string ProviderName = "coverage-recorded";
    private const string ModelName = "no-network";

    [Fact]
    public async Task OfflineB12Entry_LegacyIntentLevelBlock_IsNotCoverage_EvenWithZeroSideEffects()
    {
        var outcome = await RunOfflineEntryAsync(BuildLegacyBlockedCandidate);

        // The framework's boundary held: nothing was reviewed, saved, gated or deployed.
        outcome.Semantic["AuthoringStatus"]!.GetValue<string>().Should().Be("Blocked");
        outcome.Semantic["DraftCount"]!.GetValue<int>().Should().Be(0);
        outcome.Semantic["ReviewPassedDraftCount"]!.GetValue<int>().Should().Be(0);
        outcome.Semantic["SavedDraftCount"]!.GetValue<int>().Should().Be(0);
        outcome.Semantic["ActivationGateCalls"]!.GetValue<int>().Should().Be(0);
        outcome.Semantic["ActivationGateSuccesses"]!.GetValue<int>().Should().Be(0);
        outcome.Semantic["DeployedInventoryUnchanged"]!.GetValue<bool>().Should().BeTrue();
        outcome.Semantic["ExplicitOutcomeEntryCount"]!.GetValue<int>().Should().Be(0,
            "a blocked drafts candidate carries no unsupported reason and no clarification question");

        outcome.Semantic["Result"]!.GetValue<string>()
            .Should().Be(AssetUnsupportedIntentOracle.LegacyIntentLevelBlockAttribution);
        AssetUnsupportedIntentOracle.IsHistoricalAttribution(outcome.Label).Should().BeTrue();
        AssetUnsupportedIntentOracle.IsAcceptedResult(outcome.Label).Should().BeFalse();
        outcome.EntryException.Should().NotBeNull(
            $"the coverage assertion must reject historical attribution; observed: {outcome.Semantic}");
    }

    [Fact]
    public async Task OfflineB12Entry_ExplicitUnsupportedOutcome_IsCoverage()
    {
        var outcome = await RunOfflineEntryAsync(BuildExplicitUnsupportedRefusal);

        outcome.Semantic["AuthoringStatus"]!.GetValue<string>().Should().Be("UnsupportedIntent");
        outcome.Semantic["ExplicitOutcomeEntryCount"]!.GetValue<int>().Should().Be(1);
        outcome.Semantic["Result"]!.GetValue<string>()
            .Should().Be(AssetUnsupportedIntentOracle.ExplicitUnsupportedResult);
        AssetUnsupportedIntentOracle.IsAcceptedResult(outcome.Label).Should().BeTrue();
        outcome.EntryException.Should().BeNull(
            $"an explicit refusal with real content satisfies B12 coverage; observed: {outcome.Semantic}");

        await ZeroSideEffectsAsync(outcome);
    }

    [Fact]
    public async Task OfflineB12Entry_ExplicitClarificationOutcome_IsCoverage()
    {
        var outcome = await RunOfflineEntryAsync(BuildExplicitClarificationRequest);

        outcome.Semantic["AuthoringStatus"]!.GetValue<string>().Should().Be("NeedsClarification");
        outcome.Semantic["ExplicitOutcomeEntryCount"]!.GetValue<int>().Should().Be(1);
        outcome.Semantic["Result"]!.GetValue<string>()
            .Should().Be(AssetUnsupportedIntentOracle.ExplicitClarificationResult);
        AssetUnsupportedIntentOracle.IsAcceptedResult(outcome.Label).Should().BeTrue();
        outcome.EntryException.Should().BeNull(
            $"an explicit clarification request with real content satisfies B12 coverage; observed: {outcome.Semantic}");

        await ZeroSideEffectsAsync(outcome);
    }

    private static async Task ZeroSideEffectsAsync(OfflineEntryOutcome outcome)
    {
        outcome.Semantic["DraftCount"]!.GetValue<int>().Should().Be(0);
        outcome.Semantic["ReviewPassedDraftCount"]!.GetValue<int>().Should().Be(0);
        outcome.Semantic["SavedDraftCount"]!.GetValue<int>().Should().Be(0);
        outcome.Semantic["ActivationGateCalls"]!.GetValue<int>().Should().Be(0);
        outcome.Semantic["ActivationGateSuccesses"]!.GetValue<int>().Should().Be(0);
        outcome.Semantic["DeployedInventoryUnchanged"]!.GetValue<bool>().Should().BeTrue();
        outcome.Semantic["FabricatedCapabilityObserved"]!.GetValue<bool>().Should().BeFalse();
    }

    private static async Task<OfflineEntryOutcome> RunOfflineEntryAsync(Func<string, string> responseFactory)
    {
        var artifactPath = Path.Combine(
            Path.GetTempPath(),
            $"crest-issue159-coverage-entry-{Guid.NewGuid():N}.json");
        var client = new B12CoverageModelClient(responseFactory);
        try
        {
            var exception = await Record.ExceptionAsync(() =>
                new AssetDeepSeekLiveAuthoringEvaluationTests()
                    .EvaluateUnsupportedIntentOfflineAsync(() => client, artifactPath));

            var artifact = JsonNode.Parse(await File.ReadAllTextAsync(artifactPath))!;

            // The recorded provider must really have answered, and no request may have left the process:
            // otherwise a rejection could be an artifact of credentials, transport or budget instead of the
            // coverage rule under test.
            client.CallCount.Should().Be(1, "the recorded provider must answer the single authoring call");
            artifact["Authoring"]!["observedProvider"]!.GetValue<string>().Should().Be(ProviderName);
            artifact["Authoring"]!["observedModel"]!.GetValue<string>().Should().Be(ModelName);
            artifact["HttpResponse"]!["HttpStatus"]!.GetValue<int>().Should().Be(0,
                "a coverage-entry regression must not issue a provider request");

            var semantic = artifact["Semantic"]!;
            return new OfflineEntryOutcome(
                exception,
                semantic,
                semantic["Result"]!.GetValue<string>());
        }
        finally
        {
            if (File.Exists(artifactPath))
            {
                File.Delete(artifactPath);
            }
        }
    }

    /// <summary>
    /// A drafts answer proposing a capability the authoring allowlist does not support: the framework blocks
    /// it, so nothing deploys, yet the model never used the explicit contract.
    /// </summary>
    private static string BuildLegacyBlockedCandidate(string promptInputHash)
        => JsonSerializer.Serialize(new
        {
            contractVersion = "8a.v1",
            outcome = "drafts",
            promptInputHash,
            plan = new { planId = "coverage-legacy-block", intentText = "Cover the unsupported intent" },
            items = new object[]
            {
                new
                {
                    descriptorKind = "Capability",
                    descriptorId = "asset-management.assets.condition-scoring",
                    operation = "Create",
                    payload = new { name = "Condition scoring", version = 1 }
                }
            }
        });

    private static string BuildExplicitUnsupportedRefusal(string promptInputHash)
        => JsonSerializer.Serialize(new
        {
            contractVersion = "8a.v1",
            outcome = "unsupported",
            promptInputHash,
            unsupported = new
            {
                reasons = new object[]
                {
                    new
                    {
                        code = "CAPABILITY_UNSUPPORTED",
                        message = "Maintenance outcomes require the deployed human review task; an automatic condition-scoring algorithm is not a capability this surface can author.",
                        evidenceRefs = new[] { $"visible:workflow:{AssetContractIds.MaintenanceWorkflow}:1" }
                    }
                }
            }
        });

    private static string BuildExplicitClarificationRequest(string promptInputHash)
        => JsonSerializer.Serialize(new
        {
            contractVersion = "8a.v1",
            outcome = "needsClarification",
            promptInputHash,
            needsClarification = new
            {
                questions = new object[]
                {
                    new
                    {
                        id = "risk-threshold",
                        reasonCode = "MISSING_POLICY_INPUT",
                        question = "Which asset risk-score threshold makes auto-approval acceptable, and which policy owns it?",
                        missingInputs = new[] { "riskThreshold" },
                        candidateValues = Array.Empty<string>()
                    }
                }
            }
        });

    private sealed record OfflineEntryOutcome(
        Exception? EntryException,
        JsonNode Semantic,
        string Label);

    /// <summary>Recorded provider that echoes the real prompt input hash, so only the shape can decide.</summary>
    private sealed class B12CoverageModelClient : IDescriptorAuthoringModelClient
    {
        private readonly Func<string, string> _responseFactory;

        public B12CoverageModelClient(Func<string, string> responseFactory)
            => _responseFactory = responseFactory;

        public int CallCount { get; private set; }

        public Task<DescriptorAuthoringModelResponse> CompleteAsync(
            DescriptorAuthoringModelRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new DescriptorAuthoringModelResponse
            {
                ResponseText = _responseFactory(request.Prompt.PromptInputHash.Value),
                ProviderName = ProviderName,
                ModelName = ModelName,
                PromptInputHash = request.Prompt.PromptInputHash
            });
        }
    }
}
