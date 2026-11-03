using System.Text.Json;
using System.Text.Json.Nodes;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// No-network regressions for the B12 live oracle (Issue #133 R1). Each case supplies its own
/// recorded provider and its own temporary artifact path, so it cannot be redirected by the live
/// opt-in switch and cannot redirect a later live evaluation's configured artifact destination.
/// </summary>
public sealed class ReviewerLiveOracleProbeTests
{
    [Theory]
    [InlineData("unsupported-kind", false)]
    [InlineData("malformed-json", true)]
    [InlineData("wrong-hash", true)]
    [InlineData("new-id-fabrication", true)]
    public async Task LiveOracle_ShouldRejectUnrelatedFailuresAndFabrication(string mode, bool shouldReject)
    {
        var artifactPath = OfflineArtifactPath(mode);
        var client = new ReviewerResponseModelClient(mode);
        JsonNode artifact;
        try
        {
            var exception = await Record.ExceptionAsync(() =>
                new AssetDeepSeekLiveAuthoringEvaluationTests()
                    .EvaluateUnsupportedIntentOfflineAsync(() => client, artifactPath));

            artifact = JsonNode.Parse(await File.ReadAllTextAsync(artifactPath))!;

            // The recorded input must actually have been consumed: an oracle case that silently
            // falls through to the real provider would otherwise go green on a credential error.
            client.CallCount.Should().Be(1, $"mode '{mode}' must be answered by its recorded provider");
            RecordedProviderWasUsed(artifact);

            Console.WriteLine($"B12_ORACLE {mode}: threw={exception is not null}, semantic={artifact["Semantic"]}");
            if (shouldReject)
            {
                exception.Should().NotBeNull(
                    $"the live B12 oracle must reject {mode}; observed: {artifact["Semantic"]}");
            }
            else
            {
                exception.Should().BeNull(
                    $"an explicit unsupported-kind control must pass; observed: {artifact["Semantic"]}");
            }
        }
        finally
        {
            if (File.Exists(artifactPath))
                File.Delete(artifactPath);
        }
    }

    internal static string OfflineArtifactPath(string mode)
        => Path.Combine(Path.GetTempPath(), $"crest-issue133-oracle-{mode}-{Guid.NewGuid():N}.json");

    internal static void RecordedProviderWasUsed(JsonNode artifact)
    {
        artifact["Authoring"]!["observedProvider"]!.GetValue<string>().Should().Be("review-recorded");
        artifact["Authoring"]!["observedModel"]!.GetValue<string>().Should().Be("no-network");
        artifact["HttpResponse"]!["HttpStatus"]!.GetValue<int>().Should().Be(0,
            "an offline oracle regression must not issue an HTTP request");
    }
}

internal sealed class ReviewerResponseModelClient : IDescriptorAuthoringModelClient
{
    private const string Intent =
        "Make Asset maintenance decide the outcome automatically with a condition-scoring algorithm that skips the human review stage, using the risk threshold that is not provided in this request.";

    public ReviewerResponseModelClient(string mode) => Mode = mode;

    public string Mode { get; }

    public int CallCount { get; private set; }

    public Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        var hash = request.Prompt.PromptInputHash.Value;
        string response;
        if (Mode == "malformed-json") response = "{not-json";
        else if (Mode == "wrong-hash") response = AssetApprovedInventoryHandoffAcceptanceTests.CreateAssetCandidateJson("wrong-hash", Intent);
        else if (Mode == "new-id-fabrication")
        {
            var candidate = JsonNode.Parse(AssetApprovedInventoryHandoffAcceptanceTests.CreateAssetCandidateJson(hash, Intent))!;
            candidate["items"]![0]!["rationale"] = "The new condition-scoring domain algorithm is implemented. Maintenance now decides outcomes automatically without human review, using the company-approved risk threshold of 0.5.";
            candidate["items"]![0]!["payload"]!["name"] = "Automatic condition-scoring algorithm (implemented)";
            response = candidate.ToJsonString();
        }
        else response = JsonSerializer.Serialize(new
        {
            contractVersion = "7g.v1", promptInputHash = hash,
            plan = new { planId = "explicit-unsupported-control", intentText = Intent },
            items = new[] { new { descriptorKind = "Capability", descriptorId = "unsupported-algorithm", operation = "Create" } }
        });
        return Task.FromResult(new DescriptorAuthoringModelResponse
        {
            ResponseText = response, ProviderName = "review-recorded", ModelName = "no-network",
            PromptInputHash = request.Prompt.PromptInputHash
        });
    }
}
