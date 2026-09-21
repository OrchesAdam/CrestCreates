using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class AssetLiveCompositionTests
{
    [Fact]
    public async Task LiveOnlyWiring_ResolvesAgentAndOpenAiCompatibleClient_WithoutNetwork()
    {
        var responseObservation = new AssetLiveResponseObservation();
        await using var harness = await AssetControlPlaneApprovalHarness.CreateAsync(
            "asset-live-composition-tenant",
            "asset-live-composition-author",
            services => AssetDeepSeekLiveAuthoringEvaluationTests.ConfigureLiveServices(
                services,
                "asset-live-composition-author",
                responseObservation));

        harness.Services.GetRequiredService<IDescriptorAuthoringAgent>().Should().NotBeNull();
        harness.Services.GetRequiredService<IDescriptorAuthoringModelClient>().Should().NotBeNull();
        responseObservation.Snapshot().HttpStatus.Should().Be(0);
    }

    [Theory]
    [InlineData(null, true, 4096, "DEFAULT_OUTPUT_BUDGET")]
    [InlineData("", true, 4096, "DEFAULT_OUTPUT_BUDGET")]
    [InlineData("1", true, 1, "OUTPUT_BUDGET_ACCEPTED")]
    [InlineData("16384", true, 16384, "OUTPUT_BUDGET_ACCEPTED")]
    [InlineData("0", false, 0, "OUTPUT_BUDGET_INVALID")]
    [InlineData("16385", false, 0, "OUTPUT_BUDGET_INVALID")]
    [InlineData("4096.0", false, 0, "OUTPUT_BUDGET_INVALID")]
    [InlineData("not-an-integer", false, 0, "OUTPUT_BUDGET_INVALID")]
    public void OutputBudget_UsesInvariantBoundaries(
        string? rawValue,
        bool isValid,
        int maxOutputTokens,
        string diagnosticCode)
    {
        var resolution = AssetDeepSeekLiveAuthoringEvaluationTests.ResolveOutputBudget(rawValue);

        resolution.IsValid.Should().Be(isValid);
        resolution.MaxOutputTokens.Should().Be(maxOutputTokens);
        resolution.DiagnosticCode.Should().Be(diagnosticCode);
    }

    [Theory]
    [InlineData(200, "length", 0, true)]
    [InlineData(200, "length", 1, false)]
    [InlineData(200, "stop", 0, false)]
    [InlineData(500, "length", 0, false)]
    public void ResponseMetadata_ClassifiesOnlyBoundedOutputExhaustion(
        int status,
        string finishReason,
        int contentCharacters,
        bool expected)
    {
        var metadata = new AssetLiveResponseMetadata(
            status,
            1,
            finishReason,
            contentCharacters,
            0,
            568,
            4096,
            4096);

        AssetDeepSeekLiveAuthoringEvaluationTests.IsOutputBudgetExhausted(metadata)
            .Should().Be(expected);
    }
}
