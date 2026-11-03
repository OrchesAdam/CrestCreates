using System.Text.Json;
using System.Text.Json.Nodes;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class ReviewerLiveOracleProbeTests
{
    [Theory]
    [InlineData("unsupported-kind", false)]
    [InlineData("malformed-json", true)]
    [InlineData("wrong-hash", true)]
    [InlineData("new-id-fabrication", true)]
    public async Task LiveOracle_ShouldRejectUnrelatedFailuresAndFabrication(string mode, bool shouldReject)
    {
        ReviewerResponseModelClient.Mode = mode;
        var path = $"/tmp/crest-pr158-evidence/{mode}.json";
        Environment.SetEnvironmentVariable("CREST_ASSET_LIVE_EVAL_UNSUPPORTED_ARTIFACT", path);
        var exception = await Record.ExceptionAsync(() => new AssetDeepSeekLiveAuthoringEvaluationTests()
            .DeepSeek_UnsupportedIntent_YieldsRestrictedResult_AndFabricatesNoCapability());
        var artifact = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Console.WriteLine($"REVIEW_ORACLE {mode}: threw={exception is not null}, semantic={artifact["Semantic"]}");
        if (shouldReject)
            exception.Should().NotBeNull($"the live B12 oracle must reject {mode}; artifact: {path}");
        else
            exception.Should().BeNull($"an explicit unsupported-kind control must pass; artifact: {path}");
    }
}

internal sealed class ReviewerResponseModelClient : IDescriptorAuthoringModelClient
{
    public static string Mode { get; set; } = "unsupported-kind";
    public Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request, CancellationToken cancellationToken = default)
    {
        const string intent = "Make Asset maintenance decide the outcome automatically with a condition-scoring algorithm that skips the human review stage, using the risk threshold that is not provided in this request.";
        var hash = request.Prompt.PromptInputHash.Value;
        string response;
        if (Mode == "malformed-json") response = "{not-json";
        else if (Mode == "wrong-hash") response = AssetApprovedInventoryHandoffAcceptanceTests.CreateAssetCandidateJson("wrong-hash", intent);
        else if (Mode == "new-id-fabrication")
        {
            var candidate = JsonNode.Parse(AssetApprovedInventoryHandoffAcceptanceTests.CreateAssetCandidateJson(hash, intent))!;
            candidate["items"]![0]!["rationale"] = "The new condition-scoring domain algorithm is implemented. Maintenance now decides outcomes automatically without human review, using the company-approved risk threshold of 0.5.";
            candidate["items"]![0]!["payload"]!["name"] = "Automatic condition-scoring algorithm (implemented)";
            response = candidate.ToJsonString();
        }
        else response = JsonSerializer.Serialize(new
        {
            contractVersion = "7g.v1", promptInputHash = hash,
            plan = new { planId = "explicit-unsupported-control", intentText = intent },
            items = new[] { new { descriptorKind = "Capability", descriptorId = "unsupported-algorithm", operation = "Create" } }
        });
        return Task.FromResult(new DescriptorAuthoringModelResponse
        {
            ResponseText = response, ProviderName = "review-recorded", ModelName = "no-network",
            PromptInputHash = request.Prompt.PromptInputHash
        });
    }
}
