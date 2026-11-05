using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

/// <summary>
/// Design spec §10.1: the envelope decision matrix for wire contract 8a.v1. One test per row, so a
/// rejection is attributed to the step that produced it and a mutation can only redden its own row.
/// </summary>
public sealed class OutputParserEnvelopeMatrixTests
{
    private const string Hash = "abc123hash";
    private const string WrongHash = "WRONG";

    private const string VersionMember = "\"contractVersion\":\"8a.v1\",";
    private const string OutcomeDraftsMember = "\"outcome\":\"drafts\",";

    private const string ValidItem =
        """{"descriptorKind":"HumanTask","descriptorId":"ht_1","operation":"Create","payload":{"id":"ht_1","name":"Finance Review","version":1}}""";

    private const string ValidReason =
        """{"code":"CAPABILITY_UNSUPPORTED","message":"Needs a new domain algorithm and a governance change.","evidenceRefs":["visible:wf_asset_maintenance_review:1"]}""";

    private const string ValidQuestion =
        """{"id":"risk-threshold","reasonCode":"MISSING_POLICY_INPUT","question":"Which risk threshold decides the outcome?","missingInputs":["riskThreshold"],"candidateValues":[]}""";

    private readonly JsonDescriptorAuthoringOutputParser _parser = new();

    private static readonly DescriptorAuthoringParseContext Context = new()
    {
        TenantId = "tenant-matrix",
        AuthorId = "llm-descriptor-authoring-agent",
        AuthorKind = DescriptorDraftAuthorKind.Agent,
        CreatedAt = DateTimeOffset.UnixEpoch,
        IntentText = "Add finance review",
        ExpectedPromptInputHash = Hash
    };

    private static string Drafts(
        string leadingMembers = VersionMember + OutcomeDraftsMember + $"\"promptInputHash\":\"{Hash}\",",
        string items = "[" + ValidItem + "]",
        string extraRootMembers = "")
        => "{" + leadingMembers +
           "\"plan\":{\"planId\":\"plan_matrix\",\"intentText\":\"Add finance review\"}," +
           "\"items\":" + items + extraRootMembers + "}";

    private static string Unsupported(
        string leadingMembers = VersionMember + "\"outcome\":\"unsupported\"," + $"\"promptInputHash\":\"{Hash}\",",
        string reasons = "[" + ValidReason + "]",
        string extraRootMembers = "")
        => "{" + leadingMembers + "\"unsupported\":{\"reasons\":" + reasons + "}" + extraRootMembers + "}";

    private static string Clarification(
        string leadingMembers = VersionMember + "\"outcome\":\"needsClarification\"," + $"\"promptInputHash\":\"{Hash}\",",
        string questions = "[" + ValidQuestion + "]",
        string extraRootMembers = "")
        => "{" + leadingMembers + "\"needsClarification\":{\"questions\":" + questions + "}" + extraRootMembers + "}";

    private static string Repeated(string entry, int times)
        => "[" + string.Join(",", Enumerable.Repeat(entry, times)) + "]";

    private DescriptorAuthoringResult Rejected(string envelope, string expectedMessagePart)
    {
        var result = _parser.Parse(envelope, Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput);
        result.DraftSet.Drafts.Should().BeEmpty();
        result.Unsupported.Should().BeNull();
        result.Clarification.Should().BeNull();
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput
            && d.Severity == SeverityLevel.Error
            && d.Message.Contains(expectedMessagePart, StringComparison.Ordinal),
            $"the rejection must be attributed to the expected step, but carried [{string.Join(" | ", result.Diagnostics.Select(x => x.Message))}]");

        return result;
    }

    // ── Row 1: the legal refusal control ─────────────────────────────────────────────────────

    [Fact]
    public void Row01_OutcomeUnsupported_WithoutPlanOrItems_IsUnsupportedIntent()
    {
        var result = _parser.Parse(Unsupported(), Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.UnsupportedIntent);
        result.DraftSet.Drafts.Should().BeEmpty();
        result.Unsupported!.Reasons.Should().ContainSingle()
            .Which.Code.Should().Be(DescriptorAuthoringUnsupportedReasonCodes.CapabilityUnsupported);
        result.Unsupported.Reasons[0].EvidenceRefs.Should().Equal("visible:wf_asset_maintenance_review:1");
        result.Clarification.Should().BeNull();
        result.Plan.PlanId.Should().Be("plan_intent_unsupported");
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.IntentUnsupported
            && d.Severity == SeverityLevel.Warning);
    }

    // ── Rows 2-5: forbidden and required members, decided on the raw envelope ────────────────

    [Fact]
    public void Row02_OutcomeUnsupported_WithItemsNull_IsRejectedAsMemberPresence()
        => Rejected(Unsupported(extraRootMembers: ""","items":null"""), "must not carry the 'items' member");

    [Fact]
    public void Row03_OutcomeUnsupported_WithEmptyPlanObject_IsRejectedAsMemberPresence()
        => Rejected(Unsupported(extraRootMembers: ""","plan":{}"""), "must not carry the 'plan' member");

    [Fact]
    public void Row03b_OutcomeUnsupported_WithPlanContent_IsRejectedAsMemberPresence()
        => Rejected(
            Unsupported(extraRootMembers: ""","plan":{"planId":"p","intentText":"i"}"""),
            "must not carry the 'plan' member");

    [Fact]
    public void Row04_OutcomeDrafts_WithEmptyUnsupportedObject_IsRejectedAsMemberPresence()
        => Rejected(Drafts(extraRootMembers: ""","unsupported":{}"""), "must not carry the 'unsupported' member");

    [Fact]
    public void Row04b_OutcomeNeedsClarification_WithEmptyItemsArray_IsRejectedAsMemberPresence()
        => Rejected(Clarification(extraRootMembers: ""","items":[]"""), "must not carry the 'items' member");

    [Fact]
    public void Row05_OutcomeDrafts_WithEmptyItemsArray_IsRejectedByTheLowerBound()
        => Rejected(Drafts(items: "[]"), "no items");

    [Fact]
    public void Row05b_OutcomeUnsupported_WithoutReasonsSection_IsRejectedAsRequiredMember()
        => Rejected(
            """
            {"contractVersion":"8a.v1","outcome":"unsupported","promptInputHash":"abc123hash","unsupported":{}}
            """,
            "is missing the required 'unsupported.reasons' array");

    // ── Rows 6-8, 10, 15-16, 18: duplicate members, before any value is read ─────────────────

    [Fact]
    public void Row06_DuplicatePromptInputHash_WrongFirstRightSecond_IsRejected()
        => Rejected(
            Drafts(leadingMembers: VersionMember + OutcomeDraftsMember
                + $"\"promptInputHash\":\"{WrongHash}\",\"promptInputHash\":\"{Hash}\","),
            "duplicate member 'promptInputHash'");

    [Fact]
    public void Row06b_DuplicatePromptInputHash_WithSucceededShape_IsNeverReadAsDrafts()
    {
        var result = _parser.Parse(
            Drafts(leadingMembers: VersionMember + OutcomeDraftsMember
                + $"\"promptInputHash\":\"{Hash}\",\"promptInputHash\":\"{WrongHash}\","),
            Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput,
            "last-value-wins must not resurrect an answer that carries two hashes");
        result.DraftSet.Drafts.Should().BeEmpty();
    }

    [Fact]
    public void Row07_NeedsClarificationThenDrafts_WithNullSection_IsRejectedBeforeAnyBranch()
        => Rejected(
            Clarification(extraRootMembers: ""","outcome":"drafts","needsClarification":null"""),
            "duplicate member 'outcome'");

    [Fact]
    public void Row08_DuplicateItems_BothNonEmpty_IsRejected()
        => Rejected(
            Drafts(items: $"[{ValidItem}],\"items\":[{ValidItem}]"),
            "duplicate member 'items'");

    [Fact]
    public void Row10_DuplicateContractVersion_WrongFirstRightSecond_IsRejected()
        => Rejected(
            Drafts(leadingMembers: "\"contractVersion\":\"7g.v1\"," + VersionMember + OutcomeDraftsMember
                + $"\"promptInputHash\":\"{Hash}\","),
            "duplicate member 'contractVersion'");

    [Fact]
    public void Row15_DuplicateMemberAtThePayloadTopLevel_IsRejected()
        => Rejected(
            Drafts(items: """[{"descriptorKind":"HumanTask","descriptorId":"ht_1","operation":"Create","payload":{"id":"FIRST","id":"SECOND","name":"Finance Review","version":1}}]"""),
            "duplicate member 'id'");

    [Fact]
    public void Row16_DuplicateNestedInPayloadPlusOneWrongHash_IsDuplicateNotHashMismatch()
    {
        var result = _parser.Parse(
            Drafts(
                leadingMembers: VersionMember + OutcomeDraftsMember + $"\"promptInputHash\":\"{WrongHash}\",",
                items: """[{"descriptorKind":"HumanTask","descriptorId":"ht_1","operation":"Create","payload":{"id":"ht_1","version":1,"steps":[{"target":{"id":"FIRST","id":"SECOND"}}]}}]"""),
            Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.InvalidProviderOutput,
            "the duplicate member is decided at step 2, before the hash check at step 6");
        result.DraftSet.Drafts.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput
            && d.Message.Contains("duplicate member 'id'", StringComparison.Ordinal));
        result.Diagnostics.Should().NotContain(d => d.Code == DescriptorAuthoringDiagnosticCodes.PromptHashMismatch);
    }

    [Fact]
    public void Row18_DuplicateOutcomePlusOneWrongHash_IsDuplicateNotHashMismatch()
        => Rejected(
            Drafts(leadingMembers: VersionMember
                + "\"outcome\":\"drafts\",\"outcome\":\"unsupported\","
                + $"\"promptInputHash\":\"{WrongHash}\","),
            "duplicate member 'outcome'");

    // ── Row 9: the single-valued version boundary ────────────────────────────────────────────

    /// <summary>
    /// The deliberate <c>7g.v1</c> literal of this row is the old-version rejection fixture §12
    /// exempts from the migration sweep: it must keep the literal to prove the boundary.
    /// </summary>
    [Fact]
    public void Row09_PreviousContractVersion_IsRejectedAtTheVersionBoundary()
        => Rejected(
            Drafts(leadingMembers: "\"contractVersion\":\"7g.v1\"," + OutcomeDraftsMember + $"\"promptInputHash\":\"{Hash}\","),
            "Unsupported contract version '7g.v1'. Expected '8a.v1'.");

    // ── Rows 11 and 19: integrity before semantics ───────────────────────────────────────────

    [Fact]
    public void Row11_SingleHashMismatch_WithValidClarificationBody_IsBlocked()
    {
        var result = _parser.Parse(
            Clarification(leadingMembers: VersionMember + "\"outcome\":\"needsClarification\"," + $"\"promptInputHash\":\"{WrongHash}\","),
            Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked,
            "integrity is checked before semantics, so a wrong hash is never read as a clarification");
        result.DraftSet.Drafts.Should().BeEmpty();
        result.Plan.PlanId.Should().BeEmpty();
        result.DraftSet.DraftSetId.Should().BeEmpty();
        result.Clarification.Should().BeNull();
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.PromptHashMismatch
            && d.Severity == SeverityLevel.Blocker);
    }

    [Fact]
    public void Row19_SingleHashMismatch_WithUnknownOutcome_IsBlockedNotOutcomeRejected()
    {
        var result = _parser.Parse(
            Drafts(leadingMembers: VersionMember + "\"outcome\":\"items\"," + $"\"promptInputHash\":\"{WrongHash}\","),
            Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked);
        result.Diagnostics.Should().ContainSingle(d => d.Code == DescriptorAuthoringDiagnosticCodes.PromptHashMismatch);
    }

    // ── Rows 12-14: unknown and mis-cased members ────────────────────────────────────────────

    [Fact]
    public void Row12_UnknownRootMember_IsRejected()
        => Rejected(Drafts(extraRootMembers: ",\"confidence\":\"high\""), "unknown member 'confidence'");

    [Fact]
    public void Row12b_MisCasedRootMember_IsRejectedAsUnknown()
        => Rejected(
            Drafts(leadingMembers: VersionMember + OutcomeDraftsMember
                + $"\"promptInputHash\":\"{Hash}\",\"PromptInputHash\":\"{Hash}\","),
            "unknown member 'PromptInputHash'");

    [Fact]
    public void Row13_UnknownMemberAtItemLevel_IsRejected()
        => Rejected(
            Drafts(items: """[{"descriptorKind":"HumanTask","descriptorId":"ht_1","operation":"Create","payload":{"id":"ht_1","version":1},"extra":"surprise"}]"""),
            "unknown member 'extra'");

    [Fact]
    public void Row14_MisCasedUnsupportedSectionAlongsideClarification_IsRejected()
        => Rejected(
            Clarification(extraRootMembers: ""","Unsupported":{"reasons":[{"code":"CAPABILITY_UNSUPPORTED","message":"x"}]}"""),
            "unknown member 'Unsupported'");

    // ── Row 17: item-level required member ───────────────────────────────────────────────────

    [Fact]
    public void Row17_NullPayloadAtItem_IsRejectedAsRequiredMember()
        => Rejected(
            Drafts(items: """[{"descriptorKind":"HumanTask","descriptorId":"ht_1","operation":"Create","payload":null}]"""),
            "missing the required 'payload' object");

    // ── Rows 20-23: closed code tables, normalization and bounds ──────────────────────────────

    [Fact]
    public void Row20_UnknownUnsupportedReasonCode_IsRejected()
        => Rejected(
            Unsupported(reasons: """[{"code":"OUT_OF_SCOPE_DOMAIN","message":"Outside the declared surface."}]"""),
            "code' is outside the closed code table");

    [Fact]
    public void Row20b_UnknownClarificationReasonCode_IsRejected()
        => Rejected(
            Clarification(questions: """[{"id":"q1","reasonCode":"NEEDS_MORE_CONTEXT","question":"What is missing?","missingInputs":["threshold"]}]"""),
            "reasonCode' is outside the closed code table");

    [Fact]
    public void Row21_WhitespaceOnlyQuestion_IsRejectedAfterNormalization()
        => Rejected(
            Clarification(questions: """[{"id":"q1","reasonCode":"MISSING_POLICY_INPUT","question":"\t\n","missingInputs":["threshold"]}]"""),
            "question' must hold 1..400 characters after normalization");

    [Fact]
    public void Row21b_WhitespaceOnlyMissingInput_IsRejectedAfterNormalization()
        => Rejected(
            Clarification(questions: """[{"id":"q1","reasonCode":"MISSING_POLICY_INPUT","question":"What is missing?","missingInputs":[" "]}]"""),
            "missingInputs[0]' must hold 1..120 characters after normalization");

    [Fact]
    public void Row21c_QuestionWithoutAnyNamedGap_IsRejected()
        => Rejected(
            Clarification(questions: """[{"id":"q1","reasonCode":"MISSING_POLICY_INPUT","question":"What is missing?","missingInputs":[]}]"""),
            "missingInputs' must hold 1..8 entries");

    [Fact]
    public void Row22_DuplicateQuestionId_IsRejected()
        => Rejected(Clarification(questions: Repeated(ValidQuestion, 2)), "repeats question id 'risk-threshold'");

    [Fact]
    public void Row22b_DuplicatedIdenticalReason_IsRejected()
        => Rejected(Unsupported(reasons: Repeated(ValidReason, 2)), "repeats the same obstacle");

    [Fact]
    public void Row22c_TwoReasons_SharingOneCode_WithDifferentMessages_AreAccepted()
    {
        var result = _parser.Parse(
            Unsupported(reasons: """
                [{"code":"CAPABILITY_UNSUPPORTED","message":"Needs a new domain algorithm."},
                 {"code":"CAPABILITY_UNSUPPORTED","message":"Needs a governance decision about review."}]
                """),
            Context);

        result.Status.Should().Be(DescriptorAuthoringStatus.UnsupportedIntent);
        result.Unsupported!.Reasons.Should().HaveCount(2);
    }

    [Fact]
    public void Row23_OverBoundText_IsRejected()
        => Rejected(
            Unsupported(reasons: "[{\"code\":\"OPERATION_UNSUPPORTED\",\"message\":\"" + new string('x', 401) + "\"}]"),
            "message' must hold 1..400 characters after normalization");

    [Fact]
    public void Row23b_TooManyReasons_IsRejected()
        => Rejected(
            Unsupported(reasons: Repeated("""{"code":"OPERATION_UNSUPPORTED","message":"Needs a lifecycle operation outside Create or Update."}""", 5)),
            "reasons' must hold 1..4 entries");

    [Fact]
    public void Row23c_TooManyQuestions_IsRejected()
        => Rejected(
            Clarification(questions: Repeated("""{"id":"q1","reasonCode":"AMBIGUOUS_TARGET","question":"Which target?","missingInputs":["targetId"]}""", 5)),
            "questions' must hold 1..4 entries");

    [Fact]
    public void Row23d_TooManyCollectionEntries_IsRejected()
        => Rejected(
            Clarification(questions: """[{"id":"q1","reasonCode":"MISSING_POLICY_INPUT","question":"What is missing?","missingInputs":["a","b","c","d","e","f","g","h","i"]}]"""),
            "missingInputs' must hold 1..8 entries");

    // ── Controls and the remaining discriminator failures ─────────────────────────────────────

    [Fact]
    public void Control_LegalForms_AreThreeDistinctTerminalStates()
    {
        var drafts = _parser.Parse(Drafts(), Context);
        drafts.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        drafts.DraftSet.Drafts.Should().ContainSingle();
        drafts.Unsupported.Should().BeNull();
        drafts.Clarification.Should().BeNull();

        var clarification = _parser.Parse(Clarification(), Context);
        clarification.Status.Should().Be(DescriptorAuthoringStatus.NeedsClarification);
        clarification.DraftSet.Drafts.Should().BeEmpty();
        clarification.Plan.PlanId.Should().Be("plan_needs_clarification");
        clarification.Diagnostics.Should().ContainSingle(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.IntentNeedsClarification
            && d.Severity == SeverityLevel.Warning);

        var unsupported = _parser.Parse(Unsupported(), Context);
        unsupported.Status.Should().Be(DescriptorAuthoringStatus.UnsupportedIntent);
        unsupported.DraftSet.Drafts.Should().BeEmpty();
    }

    [Fact]
    public void Control_MissingOutcome_IsRejectedAsMissingDiscriminator()
        => Rejected(
            Drafts(leadingMembers: VersionMember + $"\"promptInputHash\":\"{Hash}\","),
            "missing the required 'outcome' discriminator");

    [Fact]
    public void Control_UnknownOutcomeValue_IsRejectedAsOutsideTheDiscriminator()
        => Rejected(
            Drafts(leadingMembers: VersionMember + "\"outcome\":\"refuse\"," + $"\"promptInputHash\":\"{Hash}\","),
            "Unsupported outcome 'refuse'");

    [Fact]
    public void Control_MissingPromptHashMember_IsRejectedBeforeTheHashCompare()
        => Rejected(
            Drafts(leadingMembers: VersionMember + OutcomeDraftsMember),
            "no usable 'promptInputHash' string member");

    [Fact]
    public void Control_NonObjectRoot_IsRejectedAsUnusableProviderOutput()
    {
        Rejected("""["an","array","root"]""", "Failed to deserialize provider output as JSON.");
        Rejected("\"prose refusal\"", "Failed to deserialize provider output as JSON.");
    }
}
