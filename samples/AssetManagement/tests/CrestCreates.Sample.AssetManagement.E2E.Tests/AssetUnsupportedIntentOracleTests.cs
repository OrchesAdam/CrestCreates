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
    // S2-R1-01: the pre-`8a.v1` route keeps its attribution label and its "nothing deployed" evidence, but it
    // carries no structured entry, so §10.7 forbids counting it as new-contract coverage.
    [Theory]
    [InlineData(DescriptorAuthoringDiagnosticCodes.UnknownDescriptorKindValue)]
    [InlineData(DescriptorAuthoringDiagnosticCodes.UnsupportedDraftOperationValue)]
    [InlineData(DescriptorAuthoringDiagnosticCodes.GovernanceBoundaryViolationValue)]
    public void IntentLevelBlock_IsHistoricalAttribution_NotNewContractCoverage(string diagnosticCode)
    {
        var result = AssetUnsupportedIntentOracle.Classify(Observe(
            DescriptorAuthoringStatus.Blocked, [diagnosticCode]));

        result.Should().Be(AssetUnsupportedIntentOracle.LegacyIntentLevelBlockAttribution);
        result.Should().NotBe(AssetUnsupportedIntentOracle.ExplicitUnsupportedResult);
        AssetUnsupportedIntentOracle.IsHistoricalAttribution(result).Should().BeTrue();
        AssetUnsupportedIntentOracle.IsAcceptedResult(result).Should().BeFalse(
            "a drafts candidate the framework itself blocked cannot show the model used the 8a.v1 contract");
    }

    [Fact]
    public void HistoricalAttribution_WithACandidateLeakingIntoReview_IsStillNotCoverage()
    {
        var result = AssetUnsupportedIntentOracle.Classify(new AssetUnsupportedIntentObservation(
            DescriptorAuthoringStatus.Blocked,
            [DescriptorAuthoringDiagnosticCodes.UnknownDescriptorKindValue],
            DraftCount: 1,
            ReviewPassedDraftCount: 1,
            ProviderOutputBudgetExhausted: false,
            ExplicitOutcomeEntryCount: 0));

        AssetUnsupportedIntentOracle.IsAcceptedResult(result).Should().BeFalse();
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
            ProviderOutputBudgetExhausted: true,
            ExplicitOutcomeEntryCount: 0);

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
            ProviderOutputBudgetExhausted: false,
            ExplicitOutcomeEntryCount: 0);

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
            ProviderOutputBudgetExhausted: false,
            ExplicitOutcomeEntryCount: 0);

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
            ProviderOutputBudgetExhausted: false,
            ExplicitOutcomeEntryCount: 0);

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
    [InlineData(DescriptorAuthoringStatus.UnsupportedIntent, 0, 0)]
    [InlineData(DescriptorAuthoringStatus.NeedsClarification, 0, 0)]
    public void NothingShortOfAnExplicitTerminalOutcomeWithContentIsEverAccepted(
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
            ProviderOutputBudgetExhausted: false,
            ExplicitOutcomeEntryCount: 0);

        var result = AssetUnsupportedIntentOracle.Classify(observation);

        result.Should().NotBe(AssetUnsupportedIntentOracle.AcceptedResult);
        result.Should().NotBe(AssetUnsupportedIntentOracle.ExplicitClarificationResult);
        AssetUnsupportedIntentOracle.IsAcceptedResult(result).Should().BeFalse(
            $"no {status} observation without an explicit terminal outcome and entry content may be accepted");
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

    // ── `8a.v1`: the two explicit terminal outcomes are attributable, but only with their own
    // diagnostic code, real entry content and no candidate artifact alongside them. ──

    [Fact]
    public void ExplicitUnsupportedOutcome_WithEntryContent_IsAccepted()
    {
        var result = AssetUnsupportedIntentOracle.Classify(Observe(
            DescriptorAuthoringStatus.UnsupportedIntent,
            [DescriptorAuthoringDiagnosticCodes.IntentUnsupportedValue],
            explicitOutcomeEntryCount: 1));

        result.Should().Be(AssetUnsupportedIntentOracle.ExplicitUnsupportedResult);
        AssetUnsupportedIntentOracle.IsAcceptedResult(result).Should().BeTrue();
    }

    [Fact]
    public void ExplicitClarificationOutcome_WithEntryContent_IsAccepted()
    {
        var result = AssetUnsupportedIntentOracle.Classify(Observe(
            DescriptorAuthoringStatus.NeedsClarification,
            [DescriptorAuthoringDiagnosticCodes.IntentNeedsClarificationValue],
            explicitOutcomeEntryCount: 2));

        result.Should().Be(AssetUnsupportedIntentOracle.ExplicitClarificationResult);
        AssetUnsupportedIntentOracle.IsAcceptedResult(result).Should().BeTrue();
    }

    [Theory]
    [InlineData(DescriptorAuthoringStatus.UnsupportedIntent, DescriptorAuthoringDiagnosticCodes.IntentUnsupportedValue)]
    [InlineData(DescriptorAuthoringStatus.NeedsClarification, DescriptorAuthoringDiagnosticCodes.IntentNeedsClarificationValue)]
    public void ExplicitOutcomeWithoutEntryContent_IsNotAccepted(DescriptorAuthoringStatus status, string code)
    {
        var noEntries = AssetUnsupportedIntentOracle.Classify(Observe(status, [code], explicitOutcomeEntryCount: 0));
        var draftsLeaked = AssetUnsupportedIntentOracle.Classify(new AssetUnsupportedIntentObservation(
            status, [code], DraftCount: 1, ReviewPassedDraftCount: 0,
            ProviderOutputBudgetExhausted: false, ExplicitOutcomeEntryCount: 1));
        var reviewPassedLeak = AssetUnsupportedIntentOracle.Classify(new AssetUnsupportedIntentObservation(
            status, [code], DraftCount: 1, ReviewPassedDraftCount: 1,
            ProviderOutputBudgetExhausted: false, ExplicitOutcomeEntryCount: 1));
        var wrongCode = AssetUnsupportedIntentOracle.Classify(Observe(
            status,
            [status == DescriptorAuthoringStatus.UnsupportedIntent
                ? DescriptorAuthoringDiagnosticCodes.IntentNeedsClarificationValue
                : DescriptorAuthoringDiagnosticCodes.IntentUnsupportedValue],
            explicitOutcomeEntryCount: 1));

        AssetUnsupportedIntentOracle.IsAcceptedResult(noEntries).Should().BeFalse(
            "a terminal status with no entry content proves nothing about the intent");
        AssetUnsupportedIntentOracle.IsAcceptedResult(draftsLeaked).Should().BeFalse(
            "an explicit refusal that also produced a draft is not a refusal");
        AssetUnsupportedIntentOracle.IsAcceptedResult(reviewPassedLeak).Should().BeFalse();
        AssetUnsupportedIntentOracle.IsAcceptedResult(wrongCode).Should().BeFalse();

        noEntries.Should().BeOneOf(
            AssetUnsupportedIntentOracle.UnsupportedOutcomeWithoutEntryPending,
            AssetUnsupportedIntentOracle.ClarificationOutcomeWithoutEntryPending);
        draftsLeaked.Should().Be(noEntries);
        reviewPassedLeak.Should().Be(noEntries);
        wrongCode.Should().Be(noEntries);
    }

    [Theory]
    [InlineData(AssetUnsupportedIntentOracle.ExplicitUnsupportedResult, true)]
    [InlineData(AssetUnsupportedIntentOracle.ExplicitClarificationResult, true)]
    [InlineData(AssetUnsupportedIntentOracle.FabricatedCapabilityClaim, false)]
    [InlineData(AssetUnsupportedIntentOracle.ArtifactsRejectedByReviewPending, false)]
    [InlineData(AssetUnsupportedIntentOracle.ProviderOutputRejectedPending, false)]
    [InlineData(AssetUnsupportedIntentOracle.PromptIntegrityFailure, false)]
    [InlineData(AssetUnsupportedIntentOracle.BlockedWithoutUnsupportedIntentDiagnoses, false)]
    [InlineData(AssetUnsupportedIntentOracle.LegacyIntentLevelBlockAttribution, false)]
    [InlineData(AssetUnsupportedIntentOracle.UnsupportedOutcomeWithoutEntryPending, false)]
    [InlineData(AssetUnsupportedIntentOracle.ClarificationOutcomeWithoutEntryPending, false)]
    [InlineData(AssetUnsupportedIntentOracle.NoArtifactProducedPending, false)]
    [InlineData(AssetUnsupportedIntentOracle.ProviderUnavailable, false)]
    [InlineData(AssetUnsupportedIntentOracle.OutputBudgetExhausted, false)]
    [InlineData(AssetUnsupportedIntentOracle.InvalidOutputBudget, false)]
    [InlineData(AssetUnsupportedIntentOracle.AuthoringFailure, false)]
    public void OnlyTheTwoExplicitLabelsAreAccepted(string label, bool accepted)
        => AssetUnsupportedIntentOracle.IsAcceptedResult(label).Should().Be(accepted);

    private static AssetUnsupportedIntentObservation Observe(
        DescriptorAuthoringStatus status,
        string[] diagnosticCodes,
        int explicitOutcomeEntryCount = 0) => new(
            status,
            diagnosticCodes,
            DraftCount: 0,
            ReviewPassedDraftCount: 0,
            ProviderOutputBudgetExhausted: false,
            explicitOutcomeEntryCount);
}
