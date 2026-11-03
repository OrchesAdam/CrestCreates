using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// Issue #133 R1 regressions: the unsupported-intent verdict must be attributable. Parse and
/// integrity failures, provider failures and approvable candidates are not B12 success, and no
/// candidate may be auto-labelled "no unsupported claim" from its descriptor id alone.
/// </summary>
public sealed class AssetUnsupportedIntentOracleTests
{
    [Theory]
    [InlineData(DescriptorAuthoringStatus.Blocked, new[] { DescriptorAuthoringDiagnosticCodes.UnknownDescriptorKindValue })]
    [InlineData(DescriptorAuthoringStatus.Blocked, new[] { DescriptorAuthoringDiagnosticCodes.UnsupportedDraftOperationValue })]
    [InlineData(DescriptorAuthoringStatus.Blocked, new[] { DescriptorAuthoringDiagnosticCodes.GovernanceBoundaryViolationValue })]
    public void IntentLevelUnsupportedDiagnosis_IsTheOnlyAcceptedResult(
        DescriptorAuthoringStatus status,
        string[] diagnosticCodes)
    {
        var result = AssetUnsupportedIntentOracle.Classify(Observe(status, diagnosticCodes));

        result.Should().Be(AssetUnsupportedIntentOracle.AcceptedResult);
        result.Should().Be(AssetUnsupportedIntentOracle.ExplicitUnsupportedResult);
    }

    [Fact]
    public void PromptHashMismatch_IsIntegrityFailure_NotUnsupportedIntent()
    {
        var result = AssetUnsupportedIntentOracle.Classify(Observe(
            DescriptorAuthoringStatus.Blocked,
            [DescriptorAuthoringDiagnosticCodes.PromptHashMismatchValue]));

        result.Should().Be(AssetUnsupportedIntentOracle.PromptIntegrityFailure);
        result.Should().NotBe(AssetUnsupportedIntentOracle.AcceptedResult);
    }

    [Fact]
    public void BlockedWithoutIntentLevelDiagnosis_IsNotAttributed()
    {
        var result = AssetUnsupportedIntentOracle.Classify(Observe(
            DescriptorAuthoringStatus.Blocked,
            [DescriptorAuthoringDiagnosticCodes.ProviderTimeoutValue]));

        result.Should().Be(AssetUnsupportedIntentOracle.BlockedWithoutUnsupportedIntentDiagnoses);
        result.Should().NotBe(AssetUnsupportedIntentOracle.AcceptedResult);
    }

    [Theory]
    [InlineData(DescriptorAuthoringStatus.InvalidProviderOutput)]
    [InlineData(DescriptorAuthoringStatus.ProviderUnavailable)]
    [InlineData(DescriptorAuthoringStatus.Failed)]
    public void InvalidOrUnavailableProviderOutput_IsNeverAClarificationVerdict(
        DescriptorAuthoringStatus status)
    {
        var result = AssetUnsupportedIntentOracle.Classify(Observe(
            status,
            [DescriptorAuthoringDiagnosticCodes.InvalidProviderOutputValue]));

        result.Should().NotBe(AssetUnsupportedIntentOracle.AcceptedResult);
        result.Should().BeOneOf(
            AssetUnsupportedIntentOracle.ProviderOutputRejectedPending,
            AssetUnsupportedIntentOracle.ProviderUnavailable,
            AssetUnsupportedIntentOracle.AuthoringFailure);
    }

    [Fact]
    public void OutputBudgetExhaustion_IsReportedSeparatelyFromUnsupportedIntent()
    {
        var observation = new AssetUnsupportedIntentObservation(
            DescriptorAuthoringStatus.InvalidProviderOutput,
            [DescriptorAuthoringDiagnosticCodes.InvalidProviderOutputValue],
            DraftCount: 0,
            ReviewPassedDraftCount: 0,
            ProviderOutputBudgetExhausted: true);

        AssetUnsupportedIntentOracle.Classify(observation)
            .Should().Be(AssetUnsupportedIntentOracle.OutputBudgetExhausted);
    }

    [Fact]
    public void ReviewPassingCandidate_IsFabricatedClaim_RegardlessOfDescriptorId()
    {
        // A brand-new descriptor id that clears deterministic review is still a claim that the
        // unsupported intent was delivered; ids carry no semantic guarantee.
        var observation = new AssetUnsupportedIntentObservation(
            DescriptorAuthoringStatus.Succeeded,
            Array.Empty<string>(),
            DraftCount: 1,
            ReviewPassedDraftCount: 1,
            ProviderOutputBudgetExhausted: false);

        AssetUnsupportedIntentOracle.Classify(observation)
            .Should().Be(AssetUnsupportedIntentOracle.FabricatedCapabilityClaim);
    }

    [Fact]
    public void CandidatesRejectedByReview_RemainPendingInsteadOfNoUnsupportedClaim()
    {
        var observation = new AssetUnsupportedIntentObservation(
            DescriptorAuthoringStatus.Succeeded,
            Array.Empty<string>(),
            DraftCount: 2,
            ReviewPassedDraftCount: 0,
            ProviderOutputBudgetExhausted: false);

        var result = AssetUnsupportedIntentOracle.Classify(observation);

        result.Should().Be(AssetUnsupportedIntentOracle.ArtifactsRejectedByReviewPending);
        result.Should().NotBe(AssetUnsupportedIntentOracle.AcceptedResult);
    }

    [Fact]
    public void SuccessfulStatusWithoutCandidate_IsPendingNotAccepted()
    {
        var observation = new AssetUnsupportedIntentObservation(
            DescriptorAuthoringStatus.SucceededWithDiagnostics,
            Array.Empty<string>(),
            DraftCount: 0,
            ReviewPassedDraftCount: 0,
            ProviderOutputBudgetExhausted: false);

        AssetUnsupportedIntentOracle.Classify(observation)
            .Should().Be(AssetUnsupportedIntentOracle.NoArtifactProducedPending);
    }

    [Theory]
    [InlineData(DescriptorAuthoringStatus.Succeeded, 1, 1)]
    [InlineData(DescriptorAuthoringStatus.Succeeded, 2, 0)]
    [InlineData(DescriptorAuthoringStatus.SucceededWithDiagnostics, 1, 1)]
    [InlineData(DescriptorAuthoringStatus.InvalidProviderOutput, 0, 0)]
    [InlineData(DescriptorAuthoringStatus.ProviderUnavailable, 0, 0)]
    [InlineData(DescriptorAuthoringStatus.Failed, 0, 0)]
    [InlineData(DescriptorAuthoringStatus.Blocked, 0, 0)]
    public void OnlyAnIntentLevelBlockIsEverAccepted(
        DescriptorAuthoringStatus status,
        int draftCount,
        int reviewPassedDraftCount)
    {
        var observation = new AssetUnsupportedIntentObservation(
            status,
            status == DescriptorAuthoringStatus.Blocked
                ? [DescriptorAuthoringDiagnosticCodes.UnknownDescriptorKindValue]
                : Array.Empty<string>(),
            draftCount,
            reviewPassedDraftCount,
            ProviderOutputBudgetExhausted: false);

        var result = AssetUnsupportedIntentOracle.Classify(observation);

        if (status == DescriptorAuthoringStatus.Blocked)
            result.Should().Be(AssetUnsupportedIntentOracle.AcceptedResult);
        else
            result.Should().NotBe(AssetUnsupportedIntentOracle.AcceptedResult);
    }

    [Fact]
    public void SanitizeClaim_CollapsesControlsAndBoundsLength()
    {
        AssetUnsupportedIntentOracle.SanitizeClaim("a\tb\u0000c  d").Should().Be("a b c d");
        AssetUnsupportedIntentOracle.SanitizeClaim("   ").Should().BeNull();
        AssetUnsupportedIntentOracle.SanitizeClaim(null).Should().BeNull();

        var longClaim = new string('x', 1_200);
        var sanitized = AssetUnsupportedIntentOracle.SanitizeClaim(longClaim, maximumCharacters: 400);

        sanitized.Should().NotBeNull();
        sanitized!.Length.Should().Be(400 + "…[truncated]".Length);
        sanitized.Should().EndWith("…[truncated]");
        sanitized.Should().StartWith(new string('x', 400));
    }

    private static AssetUnsupportedIntentObservation Observe(
        DescriptorAuthoringStatus status,
        string[] diagnosticCodes) => new(
            status,
            diagnosticCodes,
            DraftCount: 0,
            ReviewPassedDraftCount: 0,
            ProviderOutputBudgetExhausted: false);
}
