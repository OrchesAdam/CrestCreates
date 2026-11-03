using System.Text.Json.Nodes;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// Issue #133 R2 isolation regressions: the offline oracle regressions must stay offline and must
/// stay out of the live evaluation's configuration. Both properties are measured, not asserted by
/// reading the wiring code.
/// </summary>
public sealed class AssetUnsupportedIntentProbeIsolationTests
{
    private const string LiveGateVariable = "CREST_ASSET_LIVE_EVAL";
    private const string LiveArtifactVariable = "CREST_ASSET_LIVE_EVAL_UNSUPPORTED_ARTIFACT";

    [Fact]
    public async Task OfflineOracleRegression_ConsumesRecordedInputEvenWhenTheLiveSwitchIsOn()
    {
        var beforeGate = Environment.GetEnvironmentVariable(LiveGateVariable);
        var artifactPath = ReviewerLiveOracleProbeTests.OfflineArtifactPath("new-id-fabrication");
        var client = new ReviewerResponseModelClient("new-id-fabrication");
        try
        {
            Environment.SetEnvironmentVariable(LiveGateVariable, "1");

            var exception = await Record.ExceptionAsync(() =>
                new AssetDeepSeekLiveAuthoringEvaluationTests()
                    .EvaluateUnsupportedIntentOfflineAsync(() => client, artifactPath));

            // With the real provider this case could only fail as a credential error, which the
            // rejection assertion would happily accept. Consuming the recorded input is what makes
            // the rejection meaningful.
            client.CallCount.Should().Be(1, "the recorded client must answer even while the live switch is on");
            var artifact = JsonNode.Parse(await File.ReadAllTextAsync(artifactPath))!;
            ReviewerLiveOracleProbeTests.RecordedProviderWasUsed(artifact);
            exception.Should().NotBeNull(
                $"the recorded fabrication input must still be rejected; observed: {artifact["Semantic"]}");
        }
        finally
        {
            Environment.SetEnvironmentVariable(LiveGateVariable, beforeGate);
            if (File.Exists(artifactPath))
                File.Delete(artifactPath);
        }
    }

    [Fact]
    public async Task OfflineOracleRegression_LeavesTheConfiguredLiveArtifactDestinationUntouched()
    {
        var beforeArtifact = Environment.GetEnvironmentVariable(LiveArtifactVariable);
        var configuredDestination = Path.Combine(
            Path.GetTempPath(),
            $"crest-issue133-configured-live-destination-{Guid.NewGuid():N}.json");
        try
        {
            Environment.SetEnvironmentVariable(LiveArtifactVariable, configuredDestination);

            await new ReviewerLiveOracleProbeTests()
                .LiveOracle_ShouldRejectUnrelatedFailuresAndFabrication("unsupported-kind", shouldReject: false);

            Environment.GetEnvironmentVariable(LiveArtifactVariable).Should().Be(
                configuredDestination,
                "an ordinary offline regression must not redirect a later live evaluation artifact");
            File.Exists(configuredDestination).Should().BeFalse(
                "an ordinary offline regression must not write into the live evaluation destination");
        }
        finally
        {
            Environment.SetEnvironmentVariable(LiveArtifactVariable, beforeArtifact);
            if (File.Exists(configuredDestination))
                File.Delete(configuredDestination);
        }
    }
}
