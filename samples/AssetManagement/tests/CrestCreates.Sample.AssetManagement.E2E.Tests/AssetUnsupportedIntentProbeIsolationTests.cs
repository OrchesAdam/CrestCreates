using System.Text.Json.Nodes;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// Issue #133 R2/R3 isolation regressions: the offline oracle regressions must stay offline, must
/// stay out of the live evaluation's configuration, and the temporary configuration they install
/// must not be observable by a live reader. All three properties are measured, not asserted by
/// reading the wiring code. This class shares <see cref="AssetLiveEnvironmentCollection"/> with the
/// live entry and mutates the live variables only while holding <see cref="AssetLiveEnvironmentGate"/>.
/// </summary>
[Collection(AssetLiveEnvironmentCollection.Name)]
public sealed class AssetUnsupportedIntentProbeIsolationTests
{
    private const string LiveGateVariable = "CREST_ASSET_LIVE_EVAL";
    private const string LiveArtifactVariable = "CREST_ASSET_LIVE_EVAL_UNSUPPORTED_ARTIFACT";

    /// <summary>
    /// Prefix of the destination an offline regression installs for itself. A live entry must never
    /// resolve a path carrying it.
    /// </summary>
    internal const string TemporaryDestinationPrefix = "crest-issue133-configured-live-destination-";

    [Fact]
    public async Task OfflineOracleRegression_ConsumesRecordedInputEvenWhenTheLiveSwitchIsOn()
    {
        var artifactPath = ReviewerLiveOracleProbeTests.OfflineArtifactPath("new-id-fabrication");
        var client = new ReviewerResponseModelClient("new-id-fabrication");
        await using var gate = await AssetLiveEnvironmentGate.HoldAsync("offline-live-switch");
        var beforeGate = Environment.GetEnvironmentVariable(LiveGateVariable);
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
        var configuredDestination = Path.Combine(
            Path.GetTempPath(),
            $"{TemporaryDestinationPrefix}{Guid.NewGuid():N}.json");
        await using var gate = await AssetLiveEnvironmentGate.HoldAsync("offline-destination-isolation");
        var beforeArtifact = Environment.GetEnvironmentVariable(LiveArtifactVariable);
        try
        {
            Environment.SetEnvironmentVariable(LiveArtifactVariable, configuredDestination);

            await new ReviewerLiveOracleProbeTests()
                .LiveOracle_ShouldRejectUnrelatedFailuresAndFabrication("unsupported-kind", shouldReject: true);

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

    /// <summary>
    /// Scheduling half of the R3-01 fix. The gate protects direct and concurrent calls; this locks
    /// that the two classes touching the live variables are also never scheduled over each other,
    /// because a parallel real live run would otherwise read a value an offline regression installed.
    /// </summary>
    [Fact]
    public void EveryClassTouchingTheLiveEvaluationVariables_SharesTheSerialEnvironmentCollection()
    {
        var classes = new[]
        {
            typeof(AssetDeepSeekLiveAuthoringEvaluationTests),
            typeof(AssetUnsupportedIntentProbeIsolationTests),
        };

        foreach (var testClass in classes)
        {
            var collections = testClass.GetCustomAttributesData()
                .Where(attribute => attribute.AttributeType == typeof(CollectionAttribute))
                .ToArray();

            collections.Should().HaveCount(1, $"{testClass.Name} must join exactly one collection");
            collections[0].ConstructorArguments[0].Value.Should().Be(
                AssetLiveEnvironmentCollection.Name,
                $"{testClass.Name} reads or temporarily changes the live evaluation variables");
        }
    }

    /// <summary>
    /// R3-01 lock, behavioural half. The destination-isolation regression above only proved that its
    /// temporary value is restored when it exits; a live entry starting inside that window used to
    /// capture the temporary path, write its artifact there, and have that artifact deleted by the
    /// regression's cleanup. This checks the other reader instead: while the offline regression holds
    /// its temporary destination, the live entry's capture must still resolve the caller's path.
    /// Only the capture runs, so no request is issued and no credential is consumed.
    /// </summary>
    [Fact]
    public async Task LiveEntryStartingBesideTheOfflineRegression_KeepsTheCallersArtifactDestination()
    {
        var callerDestination = Path.Combine(
            Path.GetTempPath(),
            $"crest-issue133-caller-live-destination-{Guid.NewGuid():N}.json");
        var beforeArtifact = Environment.GetEnvironmentVariable(LiveArtifactVariable);
        var temporaryDestinationObserved = false;
        try
        {
            Environment.SetEnvironmentVariable(LiveArtifactVariable, callerDestination);

            for (var attempt = 0; attempt < 5 && !temporaryDestinationObserved; attempt++)
            {
                var regression = Task.Run(() => Record.ExceptionAsync(() =>
                    new AssetUnsupportedIntentProbeIsolationTests()
                        .OfflineOracleRegression_LeavesTheConfiguredLiveArtifactDestinationUntouched()));

                temporaryDestinationObserved = SpinWait.SpinUntil(
                    () => Environment.GetEnvironmentVariable(LiveArtifactVariable)?
                        .Contains(TemporaryDestinationPrefix, StringComparison.Ordinal) == true,
                    TimeSpan.FromSeconds(10));
                if (!temporaryDestinationObserved)
                {
                    (await regression).Should().BeNull("the offline regression must pass on every attempt");
                    continue;
                }

                var captured = await AssetDeepSeekLiveAuthoringEvaluationTests
                    .CaptureLiveUnsupportedEnvironmentAsync();

                captured.ArtifactPath.Should().Be(
                    callerDestination,
                    "a live entry running alongside an offline regression must not capture that " +
                    $"regression's temporary destination ({Environment.GetEnvironmentVariable(LiveArtifactVariable)})");

                (await regression).Should().BeNull(
                    "the offline regression must stay green while a live entry competes for the same configuration");
            }

            temporaryDestinationObserved.Should().BeTrue(
                "the offline regression must really install its temporary destination, otherwise this check proves nothing");
            File.Exists(callerDestination).Should().BeFalse(
                "the capture alone must not create the live evaluation artifact");
        }
        finally
        {
            Environment.SetEnvironmentVariable(LiveArtifactVariable, beforeArtifact);
            if (File.Exists(callerDestination))
                File.Delete(callerDestination);
        }
    }
}
