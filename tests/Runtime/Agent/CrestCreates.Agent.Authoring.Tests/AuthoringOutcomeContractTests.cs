using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Json;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

/// <summary>
/// Design spec §10.2: the three terminal outcomes round-trip through the generated JSON metadata,
/// <c>Snapshot()</c> preserves the new payloads, and the closure evidence asserts the required
/// contract (roots resolve, declared names round-trip) rather than the presence of annotations.
/// </summary>
public sealed class AuthoringOutcomeContractTests
{
    [Fact]
    public void UnsupportedOutcome_KeepsStructuredReasonsThroughSnapshot()
    {
        var result = Parse("""
            {"contractVersion":"8a.v1","outcome":"unsupported","promptInputHash":"abc123hash",
             "unsupported":{"reasons":[
               {"code":"OPERATION_UNSUPPORTED","message":"Removing a live workflow needs a lifecycle operation the surface does not offer."},
               {"code":"GOVERNANCE_BOUNDARY","message":"Self-approval of the change would be required.","evidenceRefs":["visible:wf_asset:1","visible:ht_asset:1"]}]}}
            """);

        result.Status.Should().Be(DescriptorAuthoringStatus.UnsupportedIntent);
        result.Unsupported!.Reasons.Should().HaveCount(2);
        result.Unsupported.Reasons[1].EvidenceRefs.Should().Equal("visible:wf_asset:1", "visible:ht_asset:1");
        result.Unsupported.Reasons[0].Message.Should().Be(
            "Removing a live workflow needs a lifecycle operation the surface does not offer.");

        var snapshot = result.Snapshot();
        snapshot.Unsupported!.Reasons.Should().HaveCount(result.Unsupported.Reasons.Count);
        snapshot.Unsupported.Reasons[0].Code.Should().Be(result.Unsupported.Reasons[0].Code);
        snapshot.Unsupported.Reasons[0].Message.Should().Be(result.Unsupported.Reasons[0].Message);
        snapshot.Unsupported.Reasons[1].EvidenceRefs.Should().Equal(
            "visible:wf_asset:1", "visible:ht_asset:1");

        // Snapshot must copy the carried lists, not share them with the returned result.
        snapshot.Unsupported.Reasons.Should()
            .NotBeSameAs(result.Unsupported.Reasons, "the outcome list is deep-copied");
        ((object)snapshot.Unsupported.Reasons[1].EvidenceRefs).Should()
            .NotBeSameAs((object)result.Unsupported.Reasons[1].EvidenceRefs, "each entry's references are copied");
        snapshot.DraftSet.Drafts.Should().BeEmpty();
    }

    [Fact]
    public void ClarificationOutcome_KeepsStructuredQuestionsThroughSnapshot()
    {
        var result = Parse("""
            {"contractVersion":"8a.v1","outcome":"needsClarification","promptInputHash":"abc123hash",
             "needsClarification":{"questions":[
               {"id":"risk-threshold","reasonCode":"MISSING_POLICY_INPUT",
                "question":"Which risk threshold decides the automatic maintenance outcome?",
                "missingInputs":["riskThreshold","policyOwner"],"candidateValues":["0.5","0.8"]},
               {"id":"target-selector","reasonCode":"MISSING_REFERENCE_SELECTION",
                "question":"Which visible workflow should the review attach to?",
                "missingInputs":["targetWorkflowId"]}]}}
            """);

        result.Status.Should().Be(DescriptorAuthoringStatus.NeedsClarification);
        var questions = result.Clarification!.Questions;
        questions.Should().HaveCount(2);
        questions[0].MissingInputs.Should().Equal("riskThreshold", "policyOwner");
        questions[0].CandidateValues.Should().Equal("0.5", "0.8");
        questions[1].CandidateValues.Should().BeEmpty();
        questions[1].Id.Should().Be("target-selector");

        var snapshot = result.Snapshot();
        snapshot.Clarification!.Questions.Should().HaveCount(2);
        snapshot.Clarification.Questions[0].Id.Should().Be("risk-threshold");
        snapshot.Clarification.Questions[0].MissingInputs.Should().Equal("riskThreshold", "policyOwner");
        snapshot.Clarification.Questions[0].CandidateValues.Should().Equal("0.5", "0.8");
        snapshot.Clarification.Questions[1].ReasonCode.Should().Be(DescriptorAuthoringClarificationReasonCodes.MissingReferenceSelection);

        // Snapshot must copy the carried lists, not share them with the returned result.
        snapshot.Clarification.Questions.Should().NotBeSameAs(questions, "the outcome list is deep-copied");
        ((object)snapshot.Clarification.Questions[0].MissingInputs).Should()
            .NotBeSameAs((object)questions[0].MissingInputs, "each question's inputs are copied");
    }

    [Fact]
    public void DraftsOutcome_CarriesNoStructuredRefusalOrClarification()
    {
        var result = Parse("""
            {"contractVersion":"8a.v1","outcome":"drafts","promptInputHash":"abc123hash",
             "plan":{"planId":"plan_contract","intentText":"Add finance review"},
             "items":[{"descriptorKind":"HumanTask","descriptorId":"ht_1","operation":"Create",
                       "payload":{"id":"ht_1","name":"Finance Review","version":1}}]}
            """);

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        result.Unsupported.Should().BeNull();
        result.Clarification.Should().BeNull();
        result.Snapshot().DraftSet.Drafts.Should().ContainSingle();
    }

    [Fact]
    public void ContractContext_ResolvesTheNewOutcomeRootsAndDeclaredWireNames()
    {
        // The production path always uses the context's own generation options; a test must not invent a
        // second option set that the framework never serializes with.
        var context = DescriptorAuthoringJsonSerializerContext.Default;

        context.GetTypeInfo(typeof(DescriptorAuthoringResult)).Should().NotBeNull();
        context.GetTypeInfo(typeof(DescriptorAuthoringUnsupportedOutcome)).Should().NotBeNull();
        context.GetTypeInfo(typeof(DescriptorAuthoringUnsupportedReason)).Should().NotBeNull();
        context.GetTypeInfo(typeof(DescriptorAuthoringClarificationOutcome)).Should().NotBeNull();
        context.GetTypeInfo(typeof(DescriptorAuthoringClarificationQuestion)).Should().NotBeNull();

        var json = JsonSerializer.Serialize(BuildUnsupportedResult(), typeof(DescriptorAuthoringResult), context);
        json.Should().Contain("\"unsupported\":{", "the refusal payload keeps its declared member name");
        json.Should().Contain("\"reasons\":[");
        json.Should().Contain("\"evidenceRefs\":[");
        json.Should().NotContain("needsClarification")
            .And.NotContain("clarification", "an unused section is omitted, not written as null");

        var roundTripped = JsonSerializer.Deserialize(json, typeof(DescriptorAuthoringResult), context);
        var deserialized = roundTripped.Should().BeOfType<DescriptorAuthoringResult>().Subject;
        deserialized.Status.Should().Be(DescriptorAuthoringStatus.UnsupportedIntent);
        deserialized.Unsupported!.Reasons[0].Code.Should().Be(DescriptorAuthoringUnsupportedReasonCodes.GovernanceBoundary);
        deserialized.Unsupported.Reasons[0].Message.Should().Be("The change would have to approve itself.");
        deserialized.Unsupported.Reasons[0].EvidenceRefs.Should().Equal("visible:wf_asset:1");
    }

    [Fact]
    public void ParserContext_ResolvesTheEnvelopeRootAndTheNestedSectionTypes()
    {
        var parserContext = JsonDescriptorAuthoringOutputParser.Context;

        parserContext.GetTypeInfo(typeof(DescriptorAuthoringProviderOutputDto)).Should().NotBeNull();
        parserContext.GetTypeInfo(typeof(DescriptorAuthoringProviderUnsupportedDto)).Should().NotBeNull();
        parserContext.GetTypeInfo(typeof(DescriptorAuthoringProviderUnsupportedReasonDto)).Should().NotBeNull();
        parserContext.GetTypeInfo(typeof(DescriptorAuthoringProviderClarificationDto)).Should().NotBeNull();
        parserContext.GetTypeInfo(typeof(DescriptorAuthoringProviderClarificationQuestionDto)).Should().NotBeNull();

        var options = parserContext.Options;
        options.AllowDuplicateProperties.Should().BeFalse("Stage B must not read the last of two duplicate members");
        options.PropertyNameCaseInsensitive.Should().BeFalse("Stage B must not bind a casing alias");
    }

    [Fact]
    public void AliasMembers_AreNotBoundEvenWhenTheRawCheckIsBypassed()
    {
        // The casing option is pinned in Stage B, so an alias stays unbound; it is rejected because the
        // raw envelope check refuses the name, not because the DTO happens to fail.
        var json = """
            {"contractVersion":"8a.v1","Outcome":"unsupported","promptInputHash":"abc123hash",
             "Unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"x"}]}}
            """;

        var dto = JsonSerializer.Deserialize(
            json,
            JsonDescriptorAuthoringOutputParser.Context.DescriptorAuthoringProviderOutputDto);

        dto!.Outcome.Should().BeNull();
        dto.Unsupported.Should().BeNull();

        var result = Parse(json);
        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        result.Diagnostics.Should().ContainSingle(d =>
            d.Message.Contains("unknown member 'Outcome'", StringComparison.Ordinal));
    }

    [Fact]
    public void StatusVocabulary_KeepsExistingNumbersAndAppendsTheTwoNewTerminals()
    {
        ((int)DescriptorAuthoringStatus.Succeeded).Should().Be(0);
        ((int)DescriptorAuthoringStatus.SucceededWithDiagnostics).Should().Be(1);
        ((int)DescriptorAuthoringStatus.Blocked).Should().Be(2);
        ((int)DescriptorAuthoringStatus.InvalidProviderOutput).Should().Be(3);
        ((int)DescriptorAuthoringStatus.ProviderUnavailable).Should().Be(4);
        ((int)DescriptorAuthoringStatus.Failed).Should().Be(5);
        ((int)DescriptorAuthoringStatus.UnsupportedIntent).Should().Be(6);
        ((int)DescriptorAuthoringStatus.NeedsClarification).Should().Be(7);
    }

    [Fact]
    public void DiagnosticCodeTable_IsClosedAndCarriesTheTwoIntentCodes()
    {
        DescriptorAuthoringDiagnosticCodes.AllValues.Should().HaveCount(14)
            .And.Contain(DescriptorAuthoringDiagnosticCodes.IntentUnsupportedValue,
                DescriptorAuthoringDiagnosticCodes.IntentNeedsClarificationValue)
            .And.OnlyContain(code => code.StartsWith("AUTHORING_", StringComparison.Ordinal));

        DescriptorAuthoringUnsupportedReasonCodes.All.Should().Equal(
            "CAPABILITY_UNSUPPORTED", "OPERATION_UNSUPPORTED", "GOVERNANCE_BOUNDARY");
        DescriptorAuthoringClarificationReasonCodes.All.Should().Equal(
            "MISSING_POLICY_INPUT", "AMBIGUOUS_TARGET", "CONFLICTING_REQUIREMENTS", "MISSING_REFERENCE_SELECTION");
    }

    private const string Hash = "abc123hash";

    private static DescriptorAuthoringParseContext Context { get; } = new()
    {
        TenantId = "tenant-contract",
        AuthorId = "llm-descriptor-authoring-agent",
        AuthorKind = DescriptorDraftAuthorKind.Agent,
        CreatedAt = DateTimeOffset.UnixEpoch,
        IntentText = "Add finance review",
        ExpectedPromptInputHash = Hash
    };

    private static DescriptorAuthoringResult Parse(string envelope)
        => new JsonDescriptorAuthoringOutputParser().Parse(envelope, Context);

    private static DescriptorAuthoringResult BuildUnsupportedResult() => new()
    {
        Status = DescriptorAuthoringStatus.UnsupportedIntent,
        Plan = new DescriptorAuthoringPlan { PlanId = "plan_intent_unsupported", IntentText = "Add finance review" },
        DraftSet = new DescriptorDraftSet { DraftSetId = "draftset_intent_unsupported" },
        Unsupported = new DescriptorAuthoringUnsupportedOutcome
        {
            Reasons =
            [
                new DescriptorAuthoringUnsupportedReason
                {
                    Code = DescriptorAuthoringUnsupportedReasonCodes.GovernanceBoundary,
                    Message = "The change would have to approve itself.",
                    EvidenceRefs = ["visible:wf_asset:1"]
                }
            ]
        },
        Diagnostics =
        [
            new DescriptorAuthoringDiagnostic
            {
                Code = DescriptorAuthoringDiagnosticCodes.IntentUnsupported,
                Message = "The change would have to approve itself.",
                Severity = SeverityLevel.Warning
            }
        ]
    };
}
